using System.Text;
using System.Text.Json;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class ClaudeTextModel(IConfiguration configuration) : ITextModel
{
    public async Task<TextModelResult> GenerateAsync(AgentProfile profile, string prompt, decimal budgetUsd, CancellationToken ct, string? responseSchema = null)
    {
        if (!configuration.GetValue<bool>("Claude:EnableRequests")) throw new ApiException(503, "provider_disabled", "Claude CLI is disabled.");
        if (profile.Provider != "claude" || profile.RevokedAt is not null || profile.TimeoutSeconds is < 5 or > 120
            || string.IsNullOrWhiteSpace(prompt) || Encoding.UTF8.GetByteCount(prompt) > profile.MaxInputBytes)
            throw new ApiException(400, "context_limit", "Invalid Claude request.");
        var arguments = new List<string> { "--restricted", "--safe-mode", "--print", "--output-format", responseSchema is null ? "text" : "json", "--tools", "",
            "--disallowedTools", "Bash,PowerShell,Read,Write,Edit,WebFetch,WebSearch,Agent,Task", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}", "--setting-sources", "", "--no-session-persistence", "--max-turns", responseSchema is null ? "1" : "3" };
        if (responseSchema is not null) { arguments.Add("--json-schema"); arguments.Add(responseSchema); }
        if (profile.Model != "default") { arguments.Add("--model"); arguments.Add(profile.Model); }
        if (profile.ReasoningEffort is { } effort) { arguments.Add("--effort"); arguments.Add(effort); }
        try
        {
            var result = await LocalCliProcess.RunAsync(configuration["Claude:Executable"] ?? "", arguments.ToArray(), prompt, profile.TimeoutSeconds, ct);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) throw new ApiException(502, "claude_failed", "Claude failed; verify login, model and CLI version.");
            if (responseSchema is null) return new(result.Output.Trim(), null, null, null);
            using var response = JsonDocument.Parse(result.Output);
            if (!response.RootElement.TryGetProperty("structured_output", out var action) || action.ValueKind != JsonValueKind.Object
                || (response.RootElement.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True))
                throw new ApiException(502, "invalid_model_response", "Claude did not return the requested structured action.");
            var usage = CliUsage.FromClaude(response.RootElement);
            return new(action.GetRawText(), usage.Input, usage.Output, null, usage.Cached);
        }
        catch (JsonException) { throw new ApiException(502, "invalid_model_response", "Claude returned an invalid structured envelope."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException(504, "provider_timeout", "Claude timed out."); }
        catch (Exception failure) when (failure is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { throw new ApiException(503, "claude_unavailable", "Configure and authenticate the Claude executable."); }
    }
}

public sealed class CliTextModel(CodexTextModel codex, ClaudeTextModel claude) : ITextModel
{
    public Task<TextModelResult> GenerateAsync(AgentProfile profile, string prompt, decimal budgetUsd, CancellationToken ct, string? responseSchema = null) => profile.Provider switch
    {
        "codex" => codex.GenerateAsync(profile, prompt, budgetUsd, ct, responseSchema),
        "claude" => claude.GenerateAsync(profile, prompt, budgetUsd, ct, responseSchema),
        _ => throw new ApiException(409, "profile_unsupported", "Unsupported local CLI.")
    };
}
