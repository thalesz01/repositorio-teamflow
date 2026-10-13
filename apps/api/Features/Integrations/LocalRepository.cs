using System.Diagnostics;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record LocalRepositoryInput(string Path, string BaseBranch);

public sealed class LocalRepository(IConfiguration configuration)
{
    public string AuthorizedPath(Guid organizationId, Guid projectId, string requested, bool requireGit = true)
    {
        if (string.IsNullOrWhiteSpace(requested) || requested.Length > 1024 || !Path.IsPathFullyQualified(requested))
            throw new ApiException(400, "invalid_repository", "An absolute local Git repository path is required.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requested));
        if (!LocalDirectoryBrowser.CanSelectRepository(configuration, organizationId, projectId, path))
            throw new ApiException(403, "repository_not_authorized", "The local path is not authorized for this project.");
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || directory.LinkTarget is not null)
                throw new ApiException(400, "invalid_repository", "Local repository must exist without directory links.");
        var git = Path.Combine(path, ".git");
        if (requireGit && (!Directory.Exists(git) || new DirectoryInfo(git).LinkTarget is not null))
            throw new ApiException(400, "invalid_repository", "A regular local Git repository is required.");
        return path;
    }

    public async Task<LocalFolderInspection> InspectAsync(Guid organizationId, Guid projectId, string requested, CancellationToken ct)
    {
        var path = AuthorizedPath(organizationId, projectId, requested, requireGit: false);
        var git = new DirectoryInfo(Path.Combine(path, ".git"));
        if (File.Exists(git.FullName)) throw new ApiException(400, "invalid_repository", "Linked Git worktrees are not supported.");
        if (!git.Exists) return new(path, false, [], null);
        if (git.LinkTarget is not null) throw new ApiException(400, "invalid_repository", "Linked Git directories are not supported.");
        var branches = await ReadGitAsync(path, ["for-each-ref", "--format=%(refname:short)", "refs/heads/"], ct);
        var current = await ReadGitAsync(path, ["symbolic-ref", "--quiet", "--short", "HEAD"], ct, allowFailure: true);
        return new(path, true, branches.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            string.IsNullOrWhiteSpace(current) ? null : current.Trim());
    }

    private static async Task<string> ReadGitAsync(string path, string[] arguments, CancellationToken ct, bool allowFailure = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var start = new ProcessStartInfo("git") { WorkingDirectory = path, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var arg in new[] { "-c", "safe.directory=" + path.Replace('\\', '/'), "--no-optional-locks" }.Concat(arguments)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new ApiException(503, "git_unavailable", "Git is unavailable.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        var result = await output; await error;
        if ((process.ExitCode != 0 && !allowFailure) || result.Length > 65536)
            throw new ApiException(400, "invalid_repository", "Cannot inspect local Git branches.");
        return process.ExitCode == 0 ? result : "";
    }

    public async Task<string> VerifyAsync(string path, string branch, CancellationToken ct)
    {
        // Validate the actual local ref before resolving its committed base.
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > 120) throw new ApiException(400, "invalid_repository", "Select a local branch.");
        await ReadGitAsync(path, ["check-ref-format", "refs/heads/" + branch], ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var arg in new[] { "-c", "safe.directory=" + path.Replace('\\', '/'), "--no-optional-locks", "rev-parse", "--verify", "refs/heads/" + branch + "^{commit}" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new ApiException(503, "git_unavailable", "Git is unavailable.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        var commit = (await output).Trim(); await error;
        if (process.ExitCode != 0 || !GitRules.IsCommit(commit))
            throw new ApiException(400, "invalid_repository", "The local base branch must contain a commit.");
        return commit;
    }
}

public sealed record LocalFolderInspection(string Path, bool HasGit, string[] Branches, string? CurrentBranch);
public sealed record LocalFolderInput(string Path, string? BaseBranch = null, Guid? Version = null);
