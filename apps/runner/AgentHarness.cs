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

/// <summary>Change edits and tests; Analysis only reads and ends with an answer (ADR 0014).</summary>
public enum HarnessMode { Change, Analysis }

// No HTTP, provider credentials or business state transitions belong in this motor.
public sealed class AgentHarness(IAgentModel model, IAgentCheckpoint checkpoints, WorkspaceTools tools, HarnessMode mode = HarnessMode.Change)
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
        // A read of a file the policy does not list is a mistake the agent can recover from when it is only answering a question.
        if (mode == HarnessMode.Change && context.Evidence.Any(e => !e.Succeeded && !TestEvidence.IsRecoverable(e))) return new(false, "tool_failed", context);
        for (var step = context.Evidence.Length + 1; step <= maxSteps; step++)
        {
            linked.Token.ThrowIfCancellationRequested();
            var action = await model.NextAsync(context, linked.Token);
            if (action.Tool == "finish" && mode == HarnessMode.Analysis)
            {
                var answered = !string.IsNullOrWhiteSpace(action.Content) && context.Evidence.Any(e => e.Tool is "read" or "search" && e.Succeeded)
                    && !context.Evidence.Any(e => e.Tool is "write" or "test");
                return new HarnessResult(answered, answered ? "analysis_answered" : "validation_missing", context);
            }
            if (action.Tool == "finish")
            {
                var validated = TestEvidence.IsValidated(context);
                return new HarnessResult(validated, validated ? "tests_passed" : "validation_missing", context);
            }
            var evidence = await tools.ExecuteAsync(step, action, linked.Token);
            context = context with { Evidence = [.. context.Evidence, evidence] };
            await checkpoints.SaveAsync(context, linked.Token);
            if (mode == HarnessMode.Change && !evidence.Succeeded && !TestEvidence.IsRecoverable(evidence)) return new HarnessResult(false, "tool_failed", context);
        }
        return new HarnessResult(false, "step_limit", context);
    }
}

public sealed class WorkspaceTools
{
    internal const int FileLimit = 16384;
    private const int MaxCreated = 20;
    private readonly ExecutionWorkspace _workspace;
    private readonly ISandbox _sandbox;
    private readonly HashSet<string> _readable;
    private readonly HashSet<string> _editable;
    private readonly SandboxCommand _test;
    private readonly System.Text.RegularExpressions.Regex[] _creatable;
    // Files this execution added; the agent may read and rewrite them without listing each one in the policy.
    private readonly HashSet<string> _created = new(StringComparer.Ordinal);

    // Paths and test commands come from trusted controller policy, never a model or repository file.
    public WorkspaceTools(ExecutionWorkspace workspace, ISandbox sandbox, string[] readable, string[] editable, SandboxCommand test, string[]? create = null, bool testsAllowed = true)
    {
        if (readable.Length > 100 || editable.Length > 100 || test.Timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Tool policy exceeds limits.");
        foreach (var path in readable.Concat(editable))
        {
            workspace.Resolve(path);
            if (path.Replace('\\', '/').Split('/').Any(p => p.StartsWith('.')))
                throw new ArgumentException("Hidden paths are not tool inputs.");
        }
        create ??= [];
        if (create.Length > 20 || create.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 240 || p.Contains("..") || Path.IsPathRooted(p)
            || p.Contains(':') || p.Contains('\\') || p.Split('/').Any(s => s.Length == 0 || s.StartsWith('.'))))
            throw new ArgumentException("Create patterns must be relative, use '/' and avoid hidden or traversing segments.");
        _creatable = create.Select(ToolPolicyPatterns.ToRegex).ToArray();
        _workspace = workspace;
        _sandbox = sandbox;
        _readable = new HashSet<string>(readable, StringComparer.Ordinal);
        _editable = new HashSet<string>(editable, StringComparer.Ordinal);
        _test = test with { ReadOnlyWorkspace = true };
        _testsAllowed = testsAllowed;
    }

    private readonly bool _testsAllowed;

    // A new file needs a matching Create pattern and an explicit "missing" hash; ordinary and build folders stay off limits.
    private bool MayCreate(AgentAction action)
    {
        if (action.Tool != "write" || action.ExpectedHash != "missing" || action.Path is not { } path || _creatable.Length == 0 || _created.Count >= MaxCreated) return false;
        var segments = path.Split('/');
        if (path.Contains('\\') || segments.Any(s => s.Length == 0 || s.StartsWith('.') || ToolPolicyPatterns.SkippedDirectories.Contains(s))) return false;
        try { if (File.Exists(_workspace.Resolve(path))) return false; }
        catch (Exception failure) when (failure is ArgumentException or InvalidDataException or IOException) { return false; }
        return _creatable.Any(pattern => pattern.IsMatch(path));
    }

    // Looks for a literal text in the readable files (and the ones this execution created). Nothing outside the policy is searched.
    private ToolEvidence Search(int step, string? query)
    {
        var needle = query?.Trim() ?? "";
        if (needle.Length is < 2 or > 200) return new(step, "search", null, false, "invalid_query");
        var matches = new List<string>();
        var scanned = 0;
        foreach (var path in _readable.Concat(_created).Distinct().Order(StringComparer.Ordinal))
        {
            if (matches.Count >= 40) break;
            string text;
            try
            {
                var full = _workspace.Resolve(path);
                var info = new FileInfo(full);
                if (!info.Exists || info.Length > FileLimit) continue;
                text = File.ReadAllText(full);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException) { continue; }
            scanned++;
            var lines = text.Split('\n');
            for (var line = 0; line < lines.Length && matches.Count < 40; line++)
                if (lines[line].Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    var shown = lines[line].Trim();
                    matches.Add($"{path}:{line + 1}: {(shown.Length > 160 ? shown[..160] : shown)}");
                }
        }
        var detail = matches.Count == 0 ? $"no matches in {scanned} readable files" : string.Join('\n', matches);
        return new(step, "search", null, true, detail.Length > 6000 ? detail[..6000] : detail);
    }

    public async Task<ToolEvidence> ExecuteAsync(int step, AgentAction action, CancellationToken ct)
    {
        if (action.Tool == "search") return Search(step, action.Content);
        if (action.Tool == "test" && !_testsAllowed) return new(step, "rejected", null, false, "tool_or_path_denied");
        if (action.Tool == "test")
        {
            var result = await _sandbox.ExecuteAsync(_workspace, _test, ct);
            return new(step, "test", null, result.ExitCode == 0 && !result.TimedOut && !result.OutputTruncated,
                JsonSerializer.Serialize(new { result.ExitCode, result.TimedOut, result.OutputTruncated, StandardOutput = TestOutput.Sanitize(result.StandardOutput), StandardError = TestOutput.Sanitize(result.StandardError), _test.Executable, _test.Arguments, _test.Timeout, _test.ReadOnlyWorkspace, environment = _sandbox.Manifest }));
        }
        if (action.Path is null || action.Tool is not ("read" or "write")
            || !((action.Tool == "read" ? _readable : _editable).Contains(action.Path) || _created.Contains(action.Path) || MayCreate(action)))
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
            _created.Add(action.Path);
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
