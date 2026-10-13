using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;

namespace TeamFlow.Runner;

public sealed record ExecutionLimits(int MaxDurationSeconds, int MaxAttempts, int MaxSteps, decimal MaxCostUsd);
public sealed record TaskSnapshot(string Title, string Description, string[] AcceptanceCriteria, Guid Version);
public sealed record ExecutionMessage(int SchemaVersion, Guid ExecutionId, Guid TaskId, Guid OrganizationId, Guid ProjectId,
    Guid WorkerId, Guid LeaseToken, DateTimeOffset LeaseExpiresAt, DateTimeOffset DeadlineAt, int Attempt,
    TaskSnapshot Snapshot, ExecutionLimits Limits, JsonElement? Repository, Guid? AgentProfileId, JsonElement? Memory = null, JsonElement? Transfer = null);

// Consumer-owned types; no dependency on the API application or its persistence models.
public sealed class RunnerClient : IRunnerControl, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly Guid _organizationId;
    private readonly Guid _projectId;
    private readonly Guid _workerId;

    public RunnerClient(Uri origin, string token, Guid organizationId, Guid projectId, Guid workerId)
    {
        if (!origin.IsAbsoluteUri || (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback))
            || origin.AbsolutePath != "/" || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new ArgumentException("Runner API must be an HTTPS origin, or HTTP loopback for development.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > 150 || token.Any(char.IsWhiteSpace)
            || organizationId == Guid.Empty || projectId == Guid.Empty || workerId == Guid.Empty)
            throw new ArgumentException("Runner credentials and expected scope are required.");
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(180), MaxResponseContentBufferSize = 128 * 1024 };
        _token = token;
        _organizationId = organizationId;
        _projectId = projectId;
        _workerId = workerId;
    }

    public async Task<ExecutionMessage?> ClaimAsync(int leaseSeconds, CancellationToken ct)
    {
        ValidateLease(leaseSeconds);
        using var response = await SendAsync("/internal/v1/executions/claim", new { leaseSeconds }, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await RunnerProtocolException.RequireSuccessAsync(response, ct);
        var message = await response.Content.ReadFromJsonAsync<ExecutionMessage>(cancellationToken: ct)
            ?? throw new InvalidDataException("Empty execution contract.");
        ValidateMessage(message, _organizationId, _projectId, _workerId, DateTimeOffset.UtcNow);
        return message;
    }

    public async Task<bool> HeartbeatAsync(ExecutionMessage message, int leaseSeconds, CancellationToken ct)
    {
        ValidateLease(leaseSeconds);
        ValidateMessage(message, _organizationId, _projectId, _workerId, DateTimeOffset.UtcNow, requireLiveLease: false);
        using var response = await SendAsync($"/internal/v1/executions/{message.ExecutionId:D}/heartbeat", new { message.LeaseToken, leaseSeconds }, ct);
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return false;
        await RunnerProtocolException.RequireSuccessAsync(response, ct);
        return true;
    }

    public async Task<CheckoutResult> PrepareRepositoryAsync(ExecutionMessage message, ExecutionWorkspace workspace,
        IRepositoryCheckout checkout, CancellationToken ct)
    {
        ValidateMessage(message, _organizationId, _projectId, _workerId, DateTimeOffset.UtcNow);
        var source = message.Repository?.Deserialize<CheckoutSource>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Execution has no repository snapshot.");
        if (source.Branch != $"teamflow/{message.ExecutionId:N}") throw new InvalidDataException("Unexpected execution branch.");
        using var response = await SendAsync($"/internal/v1/executions/{message.ExecutionId:D}/repository-token", new { message.LeaseToken }, ct);
        await RunnerProtocolException.RequireSuccessAsync(response, ct);
        var grant = await response.Content.ReadFromJsonAsync<CheckoutGrant>(cancellationToken: ct)
            ?? throw new InvalidDataException("Missing checkout credential.");
        var deadline = new[] { message.DeadlineAt, grant.ExpiresAt }.Min() - DateTimeOffset.UtcNow;
        if (deadline <= TimeSpan.Zero) throw new InvalidDataException("Checkout authorization expired.");
        using var timeout = new CancellationTokenSource(deadline);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        return await checkout.PrepareAsync(workspace, source, grant.Token, linked.Token);
    }
    public static void ValidateMessage(ExecutionMessage message, Guid organizationId, Guid projectId, Guid workerId,
        DateTimeOffset now, bool requireLiveLease = true)
    {
        if (message.SchemaVersion != 1 || message.ExecutionId == Guid.Empty || message.TaskId == Guid.Empty
            || message.OrganizationId != organizationId || message.ProjectId != projectId || message.WorkerId != workerId
            || message.LeaseToken == Guid.Empty || message.DeadlineAt <= now || message.LeaseExpiresAt > message.DeadlineAt
            || (requireLiveLease && message.LeaseExpiresAt <= now) || message.Limits is null || message.Snapshot is null)
            throw new InvalidDataException("Unsupported execution contract, scope or lease.");
        var limits = message.Limits;
        if (limits.MaxDurationSeconds is < 30 or > 3600 || limits.MaxAttempts is < 1 or > 5 || limits.MaxSteps is < 1 or > 200
            || limits.MaxCostUsd is < 0.01m or > 100m || message.Attempt < 1 || message.Attempt > limits.MaxAttempts
            || string.IsNullOrWhiteSpace(message.Snapshot.Title) || string.IsNullOrWhiteSpace(message.Snapshot.Description)
            || message.Snapshot.Version == Guid.Empty || message.Snapshot.AcceptanceCriteria is not { Length: > 0 and <= 30 })
            throw new InvalidDataException("Invalid execution snapshot or limits.");
    }

    public async Task<RemoteCheckpointReply> SaveCheckpointAsync(ExecutionMessage message, PortableContext context, CancellationToken ct)
    {
        ValidateMessage(message, _organizationId, _projectId, _workerId, DateTimeOffset.UtcNow, requireLiveLease: false);
        if (context.ExecutionId != message.ExecutionId || context.Evidence.Length > message.Limits.MaxSteps)
            throw new InvalidDataException("Checkpoint execution scope or steps mismatch.");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/internal/v1/executions/{message.ExecutionId:D}/checkpoint/")
        { Content = JsonContent.Create(new { message.LeaseToken, context }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var response = await _http.SendAsync(request, ct);
        await RunnerProtocolException.RequireSuccessAsync(response, ct);
        var saved = await response.Content.ReadFromJsonAsync<RemoteCheckpointReply>(cancellationToken: ct)
            ?? throw new InvalidDataException("Missing checkpoint acknowledgement.");
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(context, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        if (saved.Attempt != message.Attempt || saved.Sequence != context.Evidence.Length
            || saved.Context is null || saved.Context.ExecutionId != message.ExecutionId || saved.ContentHash != expectedHash)
            throw new InvalidDataException("Checkpoint acknowledgement mismatch.");
        return saved;
    }

    public async Task<RemoteRecovery> ReadRecoveryAsync(ExecutionMessage message, CancellationToken ct)
    {
        using var response = await SendAsync($"/internal/v1/executions/{message.ExecutionId:D}/recovery", new { message.LeaseToken }, ct);
        await RunnerProtocolException.RequireSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<RemoteRecovery>(cancellationToken: ct)
            ?? throw new InvalidDataException("Missing recovery plan.");
    }

    public async Task<RemoteModelReply> GenerateActionAsync(ExecutionMessage message, PortableContext context, RunnerToolPolicy policy, CancellationToken ct)
    {
        ValidateMessage(message, _organizationId, _projectId, _workerId, DateTimeOffset.UtcNow, requireLiveLease: false);
        if (context.ExecutionId != message.ExecutionId) throw new InvalidDataException("Model context scope mismatch.");
        var operationId = RunnerOperation.Id(message, "model", context.Evidence.Length);
        using var response = await SendAsync($"/internal/v1/executions/{message.ExecutionId:D}/model-turn", new
        {
            message.LeaseToken,
            operationId,
            sequence = context.Evidence.Length,
            policy = new { policy.Readable, policy.Editable }
        }, ct);
        await RunnerProtocolException.RequireSuccessAsync(response, ct);
        var reply = await response.Content.ReadFromJsonAsync<RemoteModelReply>(cancellationToken: ct)
            ?? throw new InvalidDataException("Missing model response.");
        if (reply.OperationId != operationId || reply.Action is null || reply.EstimatedCostUsd is < 0
            || reply.Action.Tool is not ("read" or "write" or "test" or "finish"))
            throw new InvalidDataException("Unsupported model action acknowledgement.");
        return reply;
    }

    public async Task<RemoteExecutionResult> SaveResultAsync(ExecutionMessage message, HarnessResult result, CancellationToken ct)
    {
        var operationId = RunnerOperation.Id(message, "result", 0);
        using var response = await SendAsync($"/internal/v1/executions/{message.ExecutionId:D}/result", new
        {
            message.LeaseToken,
            operationId,
            checkpointSequence = result.Context.Evidence.Length,
            result.Validated,
            result.Reason
        }, ct);
        await RunnerProtocolException.RequireSuccessAsync(response, ct);
        var saved = await response.Content.ReadFromJsonAsync<RemoteExecutionResult>(cancellationToken: ct)
            ?? throw new InvalidDataException("Missing execution result.");
        if (saved.ExecutionId != message.ExecutionId || saved.OperationId != operationId || saved.Validated != result.Validated
            || saved.Reason != result.Reason || saved.CheckpointSequence != result.Context.Evidence.Length)
            throw new InvalidDataException("Execution result acknowledgement mismatch.");
        return saved;
    }

    private async Task<HttpResponseMessage> SendAsync<T>(string path, T body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return await _http.SendAsync(request, ct);
    }

    private static void ValidateLease(int seconds)
    {
        if (seconds is < 5 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
    }
    public void Dispose() => _http.Dispose();
}

public sealed record CheckoutGrant(string Token, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "CheckoutGrant [redacted]";
}

public sealed record RemoteCheckpointReply(int Attempt, int Sequence, string ContentHash, DateTimeOffset CreatedAt, PortableContext Context);
