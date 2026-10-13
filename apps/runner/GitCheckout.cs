using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TeamFlow.Runner;

public sealed record CheckoutSource(string RemoteUrl, string BaseCommit, string Branch, string? LocalPath = null);
public sealed record CheckoutResult(string BaseCommit, string Branch);
public interface IRepositoryCheckout
{
    Task<CheckoutResult> PrepareAsync(ExecutionWorkspace workspace, CheckoutSource source, string token, CancellationToken ct);
}

public sealed partial class GitCheckout : IRepositoryCheckout
{
    // Only the authorized local folder is a valid source: there is no remote transport and no credential.
    public Task<CheckoutResult> PrepareAsync(ExecutionWorkspace workspace, CheckoutSource source, string token, CancellationToken ct)
    {
        if (!Commit().IsMatch(source.BaseCommit) || !Branch().IsMatch(source.Branch)) throw new ArgumentException("Invalid checkout commit or branch.");
        if (source.LocalPath is null || token != "local" || !Path.IsPathFullyQualified(source.LocalPath) || source.RemoteUrl != source.LocalPath || !Directory.Exists(source.LocalPath))
            throw new ArgumentException("Checkout requires an authorized absolute local path and pinned commit.");
        for (DirectoryInfo? directory = new(source.LocalPath); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null) throw new ArgumentException("Local checkout rejects linked directories.");
        var git = Path.Combine(source.LocalPath, ".git");
        if (!Directory.Exists(git) || new DirectoryInfo(git).LinkTarget is not null) throw new ArgumentException("A regular local Git repository is required.");
        return PrepareCoreAsync(workspace, source, ct);
    }

    internal Task<CheckoutResult> PrepareFixtureAsync(ExecutionWorkspace workspace, string fixture, string commit, Guid executionId, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(fixture) || !Directory.Exists(fixture) || !Commit().IsMatch(commit)) throw new ArgumentException("Fixture repository and commit are required.");
        return PrepareCoreAsync(workspace, new(fixture, commit, $"teamflow/{executionId:N}"), ct);
    }

    private static async Task<CheckoutResult> PrepareCoreAsync(ExecutionWorkspace workspace, CheckoutSource source, CancellationToken ct)
    {
        workspace.Resolve(".git");
        if (Directory.EnumerateFileSystemEntries(workspace.Path).Any()) throw new InvalidOperationException("Checkout requires a new empty workspace.");
        using var controller = new ExecutionWorkspace(Path.GetDirectoryName(workspace.Path) ?? throw new InvalidOperationException("Missing workspace root."));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        await RunAsync(workspace.Path, ["init", "--template=" + controller.Path, "."], controller.Path, timeout.Token, source.RemoteUrl);
        await RunAsync(workspace.Path, ["fetch", "--depth=1", "--no-tags", "--no-recurse-submodules", "--", source.RemoteUrl, source.BaseCommit], controller.Path, timeout.Token, source.RemoteUrl);
        await RunAsync(workspace.Path, ["checkout", "--no-recurse-submodules", "-b", source.Branch, "FETCH_HEAD"], controller.Path, timeout.Token, source.RemoteUrl);
        var actual = (await RunAsync(workspace.Path, ["rev-parse", "HEAD"], controller.Path, timeout.Token, source.RemoteUrl)).Trim();
        if (actual != source.BaseCommit) throw new InvalidOperationException("Checked out commit differs from the execution snapshot.");
        return new(actual, source.Branch);
    }

    private static async Task<string> RunAsync(string directory, string[] arguments, string hooksDirectory, CancellationToken ct, string authorizedSource)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_ALLOW_PROTOCOL"] = "file";
        var settings = new Dictionary<string, string>
        {
            ["core.hooksPath"] = hooksDirectory,
            ["core.symlinks"] = "false",
            ["core.protectNTFS"] = "true",
            ["core.protectHFS"] = "true",
            ["credential.helper"] = "",
            ["safe.directory"] = authorizedSource.Replace('\\', '/')
        };
        start.Environment["GIT_CONFIG_COUNT"] = settings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var index = 0;
        foreach (var (key, value) in settings)
        {
            start.Environment[$"GIT_CONFIG_KEY_{index}"] = key;
            start.Environment[$"GIT_CONFIG_VALUE_{index++}"] = value;
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
        var output = DrainAsync(process.StandardOutput); var error = DrainAsync(process.StandardError);
        try { await process.WaitForExitAsync(ct); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(output, error);
        }
        var text = await output;
        if (process.ExitCode != 0) throw new InvalidOperationException("Git checkout failed; verify repository access and pinned commit.");
        return text;
    }
    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048]; var output = new StringBuilder(); int count;
        while ((count = await reader.ReadAsync(buffer)) > 0) output.Append(buffer, 0, Math.Min(count, 16384 - output.Length));
        return output.ToString();
    }
    [GeneratedRegex(@"^[a-f0-9]{40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Commit();
    [GeneratedRegex(@"^teamflow/[a-f0-9]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Branch();
}
