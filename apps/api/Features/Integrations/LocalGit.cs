using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record PublicationFile(string Path, string Content, string Hash);
public sealed record PublicationPlan(Guid ExecutionId, string BaseCommit, string Branch, string Title,
    string Body, DateTimeOffset CreatedAt, PublicationFile[] Files);
public sealed record PublishedPullRequest(long Number, string Url, string BaseCommit, string HeadCommit, string Branch);
public sealed record GitReviewSnapshot(string HeadCommit, string BaseCommit, bool CiPassed,
    bool Merged, string? MergeCommit, string? MergeBaseParent);

// Git is a real boundary: tests replace these seams, production uses the local implementation.
public interface IGitPublisher
{
    Task<PublishedPullRequest> PublishAsync(GitConnection connection, PublicationPlan plan, CancellationToken ct);
}

public interface IGitMerge
{
    /// <summary>Fast-forwards the base branch to an already approved head; never creates a commit.</summary>
    Task MergeAsync(GitConnection connection, RepositorySnapshot snapshot, string head, CancellationToken ct);
}

public interface IGitReview
{
    Task<GitReviewSnapshot> ReadAsync(GitConnection connection, RepositorySnapshot snapshot, string expectedHead, CancellationToken ct);
}

// Git plumbing writes only objects and the execution ref; it never checks out or edits the user's files.
public sealed class LocalGit(LocalRepository local) : IGitPublisher, IGitReview, IGitMerge
{
    public async Task MergeAsync(GitConnection connection, RepositorySnapshot snapshot, string head, CancellationToken ct)
    {
        if (!GitRules.IsCommit(head)) throw new ApiException(400, "invalid_repository", "Invalid merge commit.");
        var path = local.AuthorizedPath(connection.OrganizationId, connection.ProjectId, connection.LocalPath);
        var branchRef = "refs/heads/" + snapshot.Branch;
        var execution = (await RunAsync(path, ["rev-parse", "--verify", branchRef], null, null, ct)).Trim();
        if (execution != head) throw new ApiException(409, "branch_conflict", "The execution branch no longer points to the approved commit.");
        var current = await local.VerifyAsync(path, connection.BaseBranch, ct);
        if (current == head) return;
        if (current != snapshot.BaseCommit) throw new ApiException(409, "base_changed", "The base branch moved after the review; refresh and review again.");
        var parent = (await RunAsync(path, ["rev-parse", head + "^"], null, null, ct)).Trim();
        if (parent != current) throw new ApiException(409, "merge_failed", "The approved commit is not a fast-forward of the base branch.");
        var checkedOut = (await RunAsync(path, ["symbolic-ref", "--quiet", "--short", "HEAD"], null, null, ct, allowMissing: true)).Trim();
        if (checkedOut == connection.BaseBranch)
        {
            // The base branch is the folder's current branch: only a clean tracked tree may be moved under the user's feet.
            var dirty = await RunAsync(path, ["status", "--porcelain", "--untracked-files=no"], null, null, ct);
            if (dirty.Length > 0)
                throw new ApiException(409, "merge_requires_clean_checkout", "The project folder has local changes. Commit or discard them, then try again.");
            try { await RunAsync(path, ["merge", "--ff-only", "--no-edit", head], null, null, ct); }
            catch (ApiException failure) when (failure.Code == "git_local_failed")
            { throw new ApiException(409, "merge_failed", "Git could not fast-forward the folder (for example, an untracked file would be overwritten)."); }
        }
        else
        {
            // Another branch is checked out, so no working tree depends on the base branch: move only the reference, verifying its old value.
            await RunAsync(path, ["update-ref", "refs/heads/" + connection.BaseBranch, head, current], null, null, ct);
        }
        if (await local.VerifyAsync(path, connection.BaseBranch, ct) != head) throw new ApiException(409, "merge_failed", "The base branch did not move to the approved commit.");
    }

    public async Task PrepareBaseAsync(GitConnection connection, CancellationToken ct)
    {
        var folder = await local.InspectAsync(connection.OrganizationId, connection.ProjectId, connection.LocalPath, ct);
        if (folder.Branches.Length > 0)
        {
            connection.BaseBranch = folder.CurrentBranch is { } current && folder.Branches.Contains(current) ? current : folder.Branches[0];
            connection.BaseCommit = await local.VerifyAsync(folder.Path, connection.BaseBranch, ct);
            return;
        }
        if (Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder.Path) ?? "") == Path.TrimEndingDirectorySeparator(folder.Path))
            throw new ApiException(400, "invalid_repository", "Select a project folder rather than a drive root.");
        var temporary = Path.Combine(Path.GetTempPath(), "teamflow-bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            if (!folder.HasGit) await RunAsync(folder.Path, ["init", "--initial-branch=main", "--template=" + temporary, "."], null, null, ct);
            var files = (await RunAsync(folder.Path, ["ls-files", "--cached", "--others", "--exclude-standard", "-z"], null, null, ct))
                .Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct().Where(path => IncludeInBase(folder.Path, path)).ToArray();
            if (files.Length > 5000 || files.Sum(path => new FileInfo(Path.Combine(folder.Path, path)).Length) > 200 * 1024 * 1024)
                throw new ApiException(409, "repository_tree_limit", "Initial project baseline exceeds supported limits.");
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = Path.Combine(temporary, "index"), ["GIT_LITERAL_PATHSPECS"] = "1" };
            await RunAsync(folder.Path, ["read-tree", "--empty"], null, environment, ct);
            if (files.Length > 0)
            {
                var index = new StringBuilder();
                foreach (var batch in files.Chunk(50))
                {
                    var hashes = (await RunAsync(folder.Path, new[] { "hash-object", "-w", "--no-filters", "--" }.Concat(batch).ToArray(), null, environment, ct))
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (hashes.Length != batch.Length || hashes.Any(hash => !GitRules.IsCommit(hash))) throw new ApiException(409, "git_local_failed", "Cannot prepare the initial baseline.");
                    for (var i = 0; i < batch.Length; i++) index.Append("100644 ").Append(hashes[i]).Append('\t').Append(batch[i]).Append('\0');
                }
                await RunAsync(folder.Path, ["update-index", "-z", "--index-info"], index.ToString(), environment, ct);
            }
            var tree = (await RunAsync(folder.Path, ["write-tree"], null, environment, ct)).Trim();
            foreach (var role in new[] { "AUTHOR", "COMMITTER" })
            { environment[$"GIT_{role}_NAME"] = "TeamFlow"; environment[$"GIT_{role}_EMAIL"] = "teamflow@localhost"; }
            var commit = (await RunAsync(folder.Path, ["commit-tree", tree], "TeamFlow initial project baseline\n", environment, ct)).Trim();
            var branch = folder.CurrentBranch ?? "main";
            await RunAsync(folder.Path, ["check-ref-format", "refs/heads/" + branch], null, null, ct);
            await RunAsync(folder.Path, ["update-ref", "refs/heads/" + branch, commit, new string('0', 40)], null, null, ct);
            await RunAsync(folder.Path, ["read-tree", commit], null, null, ct);
            connection.BaseBranch = branch; connection.BaseCommit = commit;
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(temporary)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static bool IncludeInBase(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (relative.Length > 240 || parts.Any(part => part.ToLowerInvariant() is ".git" or ".ssh" or ".aws" or ".azure" or ".codex" or ".docker" or ".kube" or ".npmrc" or ".pypirc" or ".netrc" or "node_modules" or "bin" or "obj" or ".venv" or "venv" or ".next"
            || part.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || part.StartsWith("id_rsa", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("id_ed25519", StringComparison.OrdinalIgnoreCase) || part.StartsWith("id_ecdsa", StringComparison.OrdinalIgnoreCase) || part.StartsWith("id_dsa", StringComparison.OrdinalIgnoreCase)
            || part.ToLowerInvariant() is "secrets.json" or "secrets.yaml" or "secrets.yml" or ".secrets" or "credentials" or "credentials.json" or "credentials.yaml" or "credentials.yml"
            || part.EndsWith(".local.json", StringComparison.OrdinalIgnoreCase))) return false;
        if (new[] { ".pem", ".key", ".pfx", ".p12", ".kdbx" }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)) return false;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        if (!File.Exists(path) || new FileInfo(path).LinkTarget is not null) return false;
        for (var directory = Directory.GetParent(path); directory is not null && directory.FullName != root; directory = directory.Parent)
            if (directory.LinkTarget is not null) return false;
        return true;
    }

    private static void Validate(GitConnection connection, PublicationPlan plan)
    {
        if (connection.RevokedAt is not null || plan.ExecutionId == Guid.Empty || !GitRules.IsCommit(plan.BaseCommit)
            || plan.Branch != $"teamflow/{plan.ExecutionId:N}" || plan.Title.Length is < 1 or > 160 || plan.Title.Any(char.IsControl)
            || Encoding.UTF8.GetByteCount(plan.Body) > 32768 || plan.Files.Length is < 1 or > 100
            || plan.Files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != plan.Files.Length
            || plan.Files.Any(f => string.IsNullOrWhiteSpace(f.Path) || f.Path.Length > 240 || f.Path.Contains('\\') || f.Path.Contains(':')
                || f.Path.StartsWith('/') || f.Path.Split('/').Any(s => s.StartsWith('.') || s.Length == 0)
                || Encoding.UTF8.GetByteCount(f.Content) > 16384
                || f.Hash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(f.Content)))))
            throw new ApiException(400, "invalid_publication", "Publication plan violates the verified file contract.");
    }

    public async Task<PublishedPullRequest> PublishAsync(GitConnection connection, PublicationPlan plan, CancellationToken ct)
    {
        Validate(connection, plan);
        var path = local.AuthorizedPath(connection.OrganizationId, connection.ProjectId, connection.LocalPath);
        if (await local.VerifyAsync(path, connection.BaseBranch, ct) != plan.BaseCommit)
            throw new ApiException(409, "base_changed", "Local base changed before publication.");
        var temporary = Path.Combine(Path.GetTempPath(), "teamflow-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = Path.Combine(temporary, "index") };
            await RunAsync(path, ["read-tree", plan.BaseCommit], null, environment, ct);
            foreach (var file in plan.Files.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                var entry = await RunAsync(path, ["ls-tree", plan.BaseCommit, "--", file.Path], null, environment, ct);
                var mode = entry.Length == 0 ? "100644" : entry.Split(' ')[0];
                if (mode is not ("100644" or "100755")) throw new ApiException(409, "unsupported_file_mode", "Cannot replace linked or special entries.");
                var parts = file.Path.Split('/');
                for (var i = 1; i < parts.Length; i++)
                {
                    var ancestor = await RunAsync(path, ["ls-tree", plan.BaseCommit, "--", string.Join('/', parts.Take(i))], null, environment, ct);
                    if (ancestor.Length > 0 && !ancestor.StartsWith("040000 tree ", StringComparison.Ordinal))
                        throw new ApiException(409, "unsupported_file_mode", "Cannot write beneath a non-directory Git entry.");
                }
                var blob = (await RunAsync(path, ["hash-object", "-w", "--no-filters", "--stdin"], file.Content, environment, ct)).Trim();
                await RunAsync(path, ["update-index", "--add", "--cacheinfo", mode, blob, file.Path], null, environment, ct);
            }
            var tree = (await RunAsync(path, ["write-tree"], null, environment, ct)).Trim();
            if (tree == (await RunAsync(path, ["rev-parse", plan.BaseCommit + "^{tree}"], null, environment, ct)).Trim())
                throw new ApiException(409, "no_changes", "No changes to publish.");
            foreach (var role in new[] { "AUTHOR", "COMMITTER" })
            {
                environment[$"GIT_{role}_NAME"] = "TeamFlow"; environment[$"GIT_{role}_EMAIL"] = "teamflow@localhost";
                environment[$"GIT_{role}_DATE"] = plan.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
            var head = (await RunAsync(path, ["commit-tree", tree, "-p", plan.BaseCommit], $"TeamFlow execution {plan.ExecutionId:D}\n", environment, ct)).Trim();
            var reference = "refs/heads/" + plan.Branch;
            var existing = (await RunAsync(path, ["rev-parse", "--verify", "--quiet", reference], null, environment, ct, allowMissing: true)).Trim();
            if (existing.Length > 0 && existing != head) throw new ApiException(409, "branch_conflict", "Execution branch changed externally.");
            if (existing.Length == 0) await RunAsync(path, ["update-ref", "--stdin"],
                $"start\nverify refs/heads/{connection.BaseBranch} {plan.BaseCommit}\ncreate {reference} {head}\nprepare\ncommit\n", environment, ct);
            return new(1, $"/api/v1/organizations/{connection.OrganizationId:D}/projects/{connection.ProjectId:D}/repository/local/reviews/{plan.ExecutionId:D}", plan.BaseCommit, head, plan.Branch);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(temporary)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(temporary, recursive: true);
        }
    }

    public async Task<GitReviewSnapshot> ReadAsync(GitConnection connection, RepositorySnapshot snapshot, string expectedHead, CancellationToken ct)
    {
        var path = local.AuthorizedPath(connection.OrganizationId, connection.ProjectId, connection.LocalPath);
        var head = (await RunAsync(path, ["rev-parse", "--verify", "refs/heads/" + snapshot.Branch], null, null, ct)).Trim();
        var currentBase = await local.VerifyAsync(path, connection.BaseBranch, ct);
        var parent = (await RunAsync(path, ["rev-parse", head + "^"], null, null, ct)).Trim();
        var verified = head == expectedHead && parent == snapshot.BaseCommit;
        // The caller also requires the persisted successful runner result for precisely this published head.
        return new(head, currentBase, verified, verified && currentBase == head,
            verified && currentBase == head ? head : null, parent);
    }

    public Task<string> DiffAsync(GitConnection connection, string baseCommit, string headCommit, CancellationToken ct)
    {
        var path = local.AuthorizedPath(connection.OrganizationId, connection.ProjectId, connection.LocalPath);
        if (!GitRules.IsCommit(baseCommit) || !GitRules.IsCommit(headCommit)) throw new ApiException(400, "invalid_repository", "Invalid diff commits.");
        return RunAsync(path, ["diff", "--no-ext-diff", "--no-textconv", baseCommit, headCommit, "--"], null, null, ct);
    }

    private static async Task<string> RunAsync(string path, string[] arguments, string? input,
        Dictionary<string, string>? environment, CancellationToken ct, bool allowMissing = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1"; start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_NO_REPLACE_OBJECTS"] = "1"; start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in new[] { "-c", "safe.directory=" + path.Replace('\\', '/'), "-c", "core.hooksPath=" + Path.Combine(Path.GetTempPath(), "teamflow-disabled-hooks"), "-c", "core.fsmonitor=false", "-c", "core.sparseCheckout=false", "-c", "commit.gpgSign=false", "-c", "gc.auto=0", "-c", "maintenance.auto=false" }) start.ArgumentList.Add(argument);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new ApiException(503, "git_unavailable", "Git unavailable.");
        var output = DrainAsync(process.StandardOutput); var error = DrainAsync(process.StandardError);
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(output, error);
        }
        var text = await output;
        if (process.ExitCode != 0 && !(allowMissing && process.ExitCode == 1)) throw new ApiException(409, "git_local_failed", "Local Git operation failed; reconcile the execution branch.");
        return text;
    }
    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var builder = new StringBuilder(); var buffer = new char[4096]; int count; var overflow = false;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            if (builder.Length + count <= 1_048_576) builder.Append(buffer, 0, count); else overflow = true;
        }
        if (overflow) throw new ApiException(409, "repository_tree_limit", "Git output exceeds the local limit.");
        return builder.ToString();
    }
}
