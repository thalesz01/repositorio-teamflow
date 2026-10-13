using System.Diagnostics;
using System.Text;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record TextModelResult(string Text, int? InputTokens, int? OutputTokens, decimal? EstimatedCostUsd);

// AI provider boundary: Codex CLI is the only implementation; tests replace it with a double.
public interface ITextModel
{
    Task<TextModelResult> GenerateAsync(AgentProfile profile, string prompt, decimal budgetUsd, CancellationToken ct);
}

// Codex provides text decisions; the existing harness remains responsible for writes and tests.
public sealed class CodexTextModel(IConfiguration configuration) : ITextModel
{
    public async Task<TextModelResult> GenerateAsync(AgentProfile profile, string prompt, decimal budgetUsd, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Codex:EnableRequests"))
            throw new ApiException(503, "provider_disabled", "Codex CLI execution is disabled.");
        var executable = configuration["Codex:Executable"] ?? "";
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)
            || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(503, "codex_not_configured", "Configure the absolute Codex executable path.");
        if (profile.RevokedAt is not null || profile.Provider != "codex" || profile.TimeoutSeconds is < 5 or > 120
            || Encoding.UTF8.GetByteCount(prompt) > profile.MaxInputBytes || string.IsNullOrWhiteSpace(prompt))
            throw new ApiException(400, "context_limit", "Invalid Codex profile or input limit.");
        var directory = Path.Combine(Path.GetTempPath(), "teamflow-codex", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var answer = Path.Combine(directory, "answer.txt");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in new[] { "--no-daemon", "exec", "--ignore-user-config", "--ignore-rules", "--ephemeral",
            "--skip-git-repo-check", "--sandbox", "read-only", "--disable", "shell_tool", "--disable", "unified_exec",
            "--color", "never", "--output-last-message", answer }) start.ArgumentList.Add(argument);
        if (profile.Model != "default") { start.ArgumentList.Add("--model"); start.ArgumentList.Add(profile.Model); }
        start.ArgumentList.Add("-");
        // Only the local login environment is inherited; server credentials are not passed to the CLI.
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "USERPROFILE", "HOME", "APPDATA", "LOCALAPPDATA", "CODEX_HOME" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start Codex.");
            var stdout = DrainAsync(process.StandardOutput);
            var stderr = DrainAsync(process.StandardError);
            try
            {
                await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stdout, stderr);
            }
            if (process.ExitCode != 0) throw new ApiException(502, "codex_failed", "Codex failed; verify local login and CLI configuration.");
            if (!File.Exists(answer) || new FileInfo(answer).Length is <= 0 or > 131072)
                throw new ApiException(502, "provider_invalid_response", "Codex returned missing or oversized output.");
            var text = await File.ReadAllTextAsync(answer, ct);
            if (string.IsNullOrWhiteSpace(text)) throw new ApiException(502, "provider_empty", "Codex returned no final message.");
            // CLI authentication does not expose monetary billing. Unknown consumption stays unknown.
            return new(text, null, null, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ApiException(504, "provider_timeout", "Codex execution timed out."); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException)
        { throw new ApiException(502, "codex_failed", "Codex process or output is unavailable."); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) > 0) { }
    }
}
