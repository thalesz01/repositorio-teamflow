using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TeamFlow.Runner;

public sealed partial class DockerSandbox
{
    private static string RootKey(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Workspace root must be absolute.");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))));
    }

    public async Task CleanupExpiredAsync(string workspaceRoot, CancellationToken ct)
    {
        var rootKey = RootKey(workspaceRoot);
        using var list = Start(["ps", "-a", "--filter", "label=teamflow.root=" + rootKey, "--format", "{{.Names}}"]);
        var output = CaptureAsync(list.StandardOutput);
        var errors = CaptureAsync(list.StandardError);
        try { await list.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { if (!list.HasExited) list.Kill(true); throw; }
        var names = await output;
        await errors;
        if (list.ExitCode != 0 || names.Truncated) throw new InvalidOperationException("Cannot inventory owned sandbox recovery.");
        foreach (var name in names.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Regex.IsMatch(name, @"\Ateamflow-[a-f0-9]{32}\z", RegexOptions.CultureInvariant)) continue;
            using var inspect = Start(["inspect", "--format", "{{json .Config.Labels}}", name]);
            var labels = CaptureAsync(inspect.StandardOutput);
            var failure = CaptureAsync(inspect.StandardError);
            try { await inspect.WaitForExitAsync(ct); }
            catch (OperationCanceledException) { if (!inspect.HasExited) inspect.Kill(true); throw; }
            var data = await labels;
            await failure;
            if (inspect.ExitCode != 0) continue; // Another controller may already have removed its expired sandbox.
            using var document = JsonDocument.Parse(data.Text);
            var fields = document.RootElement;
            if (fields.TryGetProperty("teamflow.owner", out var owner) && owner.GetString() == name
                && fields.TryGetProperty("teamflow.root", out var scope) && scope.GetString() == rootKey
                && fields.TryGetProperty("teamflow.expires", out var expiry)
                && long.TryParse(expiry.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                && seconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) await RemoveOwnedContainerAsync(name);
        }
    }
}
