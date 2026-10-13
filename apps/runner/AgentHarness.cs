using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeamFlow.Runner;

public sealed record AgentAction(string Tool, string? Path = null, string? Content = null, string? ExpectedHash = null);
public sealed record ToolEvidence(int Step, string Tool, string? Path, bool Succeeded, string Detail, string? Hash = null);
public sealed record PortableContext(int SchemaVersion, Guid ExecutionId, string Objective, string[] AcceptanceCriteria,
    string BaseCommit, ToolEvidence[] Evidence, string[] Pending,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Memory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? History = null);
public sealed record HarnessResult(bool Validated, string Reason, PortableContext Context);

public interface IAgentModel
{
    Task<AgentAction> NextAsync(PortableContext context, CancellationToken ct);
}

public interface IAgentCheckpoint
{
    Task SaveAsync(PortableContext context, CancellationToken ct);
}

// No HTTP, provider credentials or business state transitions belong in this motor.
public sealed class AgentHarness(IAgentModel model, IAgentCheckpoint checkpoints, WorkspaceTools tools)
{
    public async Task<HarnessResult> RunAsync(PortableContext context, int maxSteps, TimeSpan duration, CancellationToken ct)
    {
        if (context.SchemaVersion != 1 || context.ExecutionId == Guid.Empty || context.Evidence.Length > maxSteps
            || string.IsNullOrWhiteSpace(context.Objective) || context.AcceptanceCriteria.Length == 0
            || context.BaseCommit.Length != 40 || !context.BaseCommit.All(Uri.IsHexDigit)
            || maxSteps is < 1 or > 200 || duration < TimeSpan.FromMilliseconds(100) || duration > TimeSpan.FromHours(1))
            throw new ArgumentException("Invalid initial harness context or limits.");
        using var timeout = new CancellationTokenSource(duration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        await checkpoints.SaveAsync(context, linked.Token);
        if (context.Evidence.Any(e => !e.Succeeded)) return new(false, "tool_failed", context);
        for (var step = context.Evidence.Length + 1; step <= maxSteps; step++)
        {
            linked.Token.ThrowIfCancellationRequested();
            var action = await model.NextAsync(context, linked.Token);
            if (action.Tool == "finish")
            {
                var lastEdit = Array.FindLastIndex(context.Evidence, e => e.Tool == "write" && e.Succeeded);
                var validated = lastEdit >= 0 && context.Evidence.Skip(lastEdit + 1).Any(e => e.Tool == "test" && e.Succeeded)
                    && context.Evidence.All(e => e.Succeeded) && context.Pending.Length == 0;
                return new HarnessResult(validated, validated ? "tests_passed" : "validation_missing", context);
            }
            var evidence = await tools.ExecuteAsync(step, action, linked.Token);
            context = context with { Evidence = [.. context.Evidence, evidence] };
            await checkpoints.SaveAsync(context, linked.Token);
            if (!evidence.Succeeded) return new HarnessResult(false, "tool_failed", context);
        }
        return new HarnessResult(false, "step_limit", context);
    }
}

public sealed class WorkspaceTools
{
    private const int FileLimit = 16384;
    private readonly ExecutionWorkspace _workspace;
    private readonly ISandbox _sandbox;
    private readonly HashSet<string> _readable;
    private readonly HashSet<string> _editable;
    private readonly SandboxCommand _test;

    // Paths and test commands come from trusted controller policy, never a model or repository file.
    public WorkspaceTools(ExecutionWorkspace workspace, ISandbox sandbox, string[] readable, string[] editable, SandboxCommand test)
    {
        if (readable.Length > 100 || editable.Length > 100 || test.Timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Tool policy exceeds limits.");
        foreach (var path in readable.Concat(editable))
        {
            workspace.Resolve(path);
            if (path.Replace('\\', '/').Split('/').Any(p => p.StartsWith('.')))
                throw new ArgumentException("Hidden paths are not tool inputs.");
        }
        _workspace = workspace;
        _sandbox = sandbox;
        _readable = new HashSet<string>(readable, StringComparer.Ordinal);
        _editable = new HashSet<string>(editable, StringComparer.Ordinal);
        _test = test with { ReadOnlyWorkspace = true };
    }

    public async Task<ToolEvidence> ExecuteAsync(int step, AgentAction action, CancellationToken ct)
    {
        if (action.Tool == "test")
        {
            var result = await _sandbox.ExecuteAsync(_workspace, _test, ct);
            return new(step, "test", null, result.ExitCode == 0 && !result.TimedOut && !result.OutputTruncated,
                JsonSerializer.Serialize(new { result.ExitCode, result.TimedOut, result.OutputTruncated, _test.Executable, _test.Arguments, _test.Timeout, _test.ReadOnlyWorkspace, environment = _sandbox.Manifest }));
        }
        if (action.Path is null || !(action.Tool == "read" ? _readable : _editable).Contains(action.Path)
            || action.Tool is not ("read" or "write"))
            return new(step, "rejected", null, false, "tool_or_path_denied");
        var path = _workspace.Resolve(action.Path);
        if (action.Tool == "write" && !File.Exists(path) && action.ExpectedHash == "missing")
        {
            if (action.Content is null || Encoding.UTF8.GetByteCount(action.Content) > FileLimit)
                return new(step, "write", action.Path, false, "invalid_content");
            var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("Missing file parent.");
            Directory.CreateDirectory(parent);
            var content = Encoding.UTF8.GetBytes(action.Content);
            await using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await created.WriteAsync(content, ct);
            return new(step, "write", action.Path, true, "file_created", Convert.ToHexStringLower(SHA256.HashData(content)));
        }
        if (action.Tool == "read" && !File.Exists(path))
            return new(step, "read", action.Path, true, "file_missing; create with expectedHash=missing");
        if (!File.Exists(path) || new FileInfo(path).Length > FileLimit)
            return new(step, action.Tool, action.Path, false, "file_missing_or_too_large");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.Length > FileLimit) return new(step, action.Tool, action.Path, false, "file_too_large");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (action.Tool == "read")
            return new(step, "read", action.Path, true, Encoding.UTF8.GetString(bytes), hash);
        if (action.ExpectedHash != hash || action.Content is null || Encoding.UTF8.GetByteCount(action.Content) > FileLimit)
            return new(step, "write", action.Path, false, "hash_conflict_or_invalid_content", hash);
        await File.WriteAllTextAsync(path, action.Content, new UTF8Encoding(false), ct);
        return new(step, "write", action.Path, true, "file_updated",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(action.Content))));
    }
}
