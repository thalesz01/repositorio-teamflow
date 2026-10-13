using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed record PublicationStatus(string State, int Attempts, string? FailureCode, PublishedPullRequest? PullRequest);

public sealed class PublicationAccess(AppDbContext db, TaskAccess tasks, RepositoryAccess repositories,
    IGitPublisher publisher, TimeProvider clock, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public async Task<PublicationStatus> ReadAsync(Guid organizationId, Guid projectId, Guid taskId, Guid id,
        ClaimsPrincipal user, CancellationToken ct)
    {
        await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        var execution = await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId
            && e.ProjectId == projectId && e.TaskId == taskId, ct) ?? throw new ApiException(404, "execution_not_found", "Execution not found.");
        return Status(execution);
    }

    public async Task<PublicationStatus> PublishAsync(Guid id, CancellationToken ct)
    {
        var sourceJson = await db.Executions.Where(e => e.Id == id).Select(e => e.RepositorySnapshot).SingleOrDefaultAsync(ct);
        var local = sourceJson is not null && JsonSerializer.Deserialize<RepositorySnapshot>(sourceJson, JsonOptions)?.LocalPath is not null;
        if (!(local ? configuration.GetValue<bool>("Local:EnablePublication") : configuration.GetValue<bool>("GitHub:EnablePublication")))
            throw new ApiException(503, "publication_disabled", "External publication is disabled on this server.");
        Execution execution;
        var lease = Guid.NewGuid();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            execution = await db.Executions.FromSqlInterpolated($"SELECT * FROM \"Executions\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
                ?? throw new ApiException(404, "execution_not_found", "Execution not found.");
            if (execution.PublicationState == "published") return Status(execution);
            if (execution.State != ExecutionState.Succeeded || execution.ResultCode != "tests_passed")
                throw new ApiException(409, "validation_required", "Publication requires a verified successful execution.");
            if (execution.PublicationLeaseUntil > clock.GetUtcNow())
                throw new ApiException(409, "publication_in_progress", "Another publisher owns this execution.");
            if (execution.PublicationAttempts >= 3)
                throw new ApiException(409, "publication_attempt_limit", "Publication retry limit reached; operator reconciliation is required.");
            execution.PublicationState = "publishing";
            execution.PublicationAttempts++;
            execution.PublicationLease = lease;
            execution.PublicationLeaseUntil = clock.GetUtcNow().AddMinutes(3);
            execution.PublicationFailureCode = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var checkpoint = await db.ExecutionCheckpoints.AsNoTracking().SingleOrDefaultAsync(c => c.ExecutionId == id
                && c.OrganizationId == execution.OrganizationId && c.ProjectId == execution.ProjectId && c.Attempt == execution.Attempt
                && c.Sequence == execution.ResultCheckpointSequence, timeout.Token)
                ?? throw new ApiException(409, "publication_evidence_missing", "Validated checkpoint is unavailable.");
            var context = JsonSerializer.Deserialize<CheckpointContext>(checkpoint.Context, JsonOptions)
                ?? throw new InvalidDataException("Invalid checkpoint.");
            var lastEdit = Array.FindLastIndex(context.Evidence, e => e.Tool == "write" && e.Succeeded);
            if (lastEdit < 0 || context.Pending.Length != 0 || context.Evidence.Any(e => !e.Succeeded)
                || !context.Evidence.Skip(lastEdit + 1).Any(e => e.Tool == "test" && e.Succeeded))
                throw new ApiException(409, "validation_required", "Verified write and later successful tests are required.");
            var receipts = await db.ModelTurns.AsNoTracking().Where(t => t.ExecutionId == id && t.OrganizationId == execution.OrganizationId
                && t.ProjectId == execution.ProjectId).OrderByDescending(t => t.Attempt).Take(200).ToListAsync(timeout.Token);
            if (receipts.Any(t => t.State != "completed" || (t.BillingMode == "api" && t.EstimatedCostUsd == null)))
                throw new ApiException(409, "model_outcome_uncertain", "Uncertain model operations prevent publication.");
            var files = execution.TransferSnapshot is null ? new List<PublicationFile>()
                : (JsonSerializer.Deserialize<PortableTransfer>(execution.TransferSnapshot, JsonOptions)?.Writes ?? [])
                    .Select(w => new PublicationFile(w.Path, w.Content, w.Hash)).ToList();
            foreach (var write in context.Evidence.Where(e => e.Tool == "write").GroupBy(e => e.Path).Select(g => g.Last()))
            {
                var actions = receipts.Where(t => t.Sequence == write.Step - 1 && t.Action != null)
                    .Select(t => JsonSerializer.Deserialize<ModelAction>(t.Action ?? throw new InvalidDataException("Missing action."), JsonOptions));
                var action = actions.FirstOrDefault(a => a?.Tool == "write" && a.Path == write.Path && a.Content != null
                    && Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(a.Content))) == write.Hash);
                if (action?.Content is null || write.Path is null || write.Hash is null)
                    throw new ApiException(409, "publication_evidence_missing", "Confirmed file receipt is unavailable.");
                files.RemoveAll(f => f.Path == write.Path);
                files.Add(new(write.Path, action.Content, write.Hash));
            }
            var task = JsonSerializer.Deserialize<TaskDto>(execution.TaskSnapshot, JsonOptions) ?? throw new InvalidDataException("Invalid task snapshot.");
            var repository = JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot ?? "null", JsonOptions)
                ?? throw new ApiException(409, "repository_not_configured", "Execution has no repository snapshot.");
            if (context.BaseCommit != repository.BaseCommit) throw new ApiException(409, "base_changed", "Evidence base differs from repository snapshot.");
            var body = $"{task.Description}\n\nAcceptance criteria:\n" + string.Join("\n", task.AcceptanceCriteria.Select(c => "- " + c))
                + $"\n\nExecution: {id:D}\nBase: {repository.BaseCommit}\nCheckpoint: {checkpoint.ContentHash}"
                + "\n\nChanges:\n" + string.Join("\n", files.Select(f => $"- {f.Path}: SHA-256 {f.Hash}"))
                + "\n\nSuccessful controller tests: " + context.Evidence.Count(e => e.Tool == "test" && e.Succeeded)
                + "\nPending: none. Tool outputs omitted. Human review and configured CI are still required.";
            var plan = new PublicationPlan(id, repository.BaseCommit, repository.Branch, task.Title, body, execution.CreatedAt, files.ToArray());
            var published = await repositories.PublishAsync(execution.OrganizationId, execution.ProjectId, repository, plan, publisher, timeout.Token);
            await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
            await db.Entry(execution).ReloadAsync(timeout.Token);
            if (execution.PublicationLease != lease || execution.PublicationLeaseUntil <= clock.GetUtcNow())
                throw new ApiException(409, "publication_lease_lost", "Publication lease was lost; reconcile the external PR.");
            if (await tasks.ApplyPrPublishedAsync(execution.OrganizationId, execution.ProjectId, execution.TaskId, timeout.Token) != 1)
                throw new ApiException(409, "publication_task_changed", "Task no longer accepts this publication.");
            execution.PublicationState = "published";
            execution.PullRequestNumber = published.Number;
            execution.PullRequestUrl = published.Url;
            execution.PublishedHeadCommit = published.HeadCommit;
            execution.PublicationLeaseUntil = null;
            await db.SaveChangesAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
            return Status(execution);
        }
        catch (Exception error)
        {
            var code = error is ApiException api ? api.Code : error is OperationCanceledException ? "publication_timeout" : "publication_failed";
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await db.Executions.Where(e => e.Id == id && e.PublicationLease == lease && e.PublicationState == "publishing")
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.PublicationState, "failed").SetProperty(e => e.PublicationFailureCode, code)
                    .SetProperty(e => e.PublicationLeaseUntil, (DateTimeOffset?)null), cleanup.Token);
            throw;
        }
    }

    public async Task<string> LocalDiffAsync(Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        var execution = await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id
            && e.OrganizationId == organizationId && e.ProjectId == projectId, ct)
            ?? throw new ApiException(404, "execution_not_found", "Execution not found.");
        await tasks.RequireAsync(organizationId, projectId, execution.TaskId, user, ct);
        if (execution.PublicationState != "published" || execution.PublishedHeadCommit is not { } head)
            throw new ApiException(409, "publication_required", "A confirmed local publication is required.");
        var source = JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot ?? "null", JsonOptions)
            ?? throw new ApiException(409, "repository_not_configured", "Repository not configured.");
        var task = JsonSerializer.Deserialize<TaskDto>(execution.TaskSnapshot, JsonOptions) ?? throw new InvalidDataException("Invalid task snapshot.");
        return $"Title: {task.Title}\n{task.Description}\n\nAcceptance criteria:\n"
            + string.Join("\n", task.AcceptanceCriteria.Select(c => "- " + c))
            + $"\n\nTests: {execution.ResultCode}\nExecution: {id:D}\nBranch: {source.Branch}\nBase: {source.BaseCommit}\nHead: {head}\n\n"
            + await repositories.LocalDiffAsync(organizationId, projectId, source, head, ct);
    }

    private static PublicationStatus Status(Execution execution)
    {
        PublishedPullRequest? receipt = null;
        if (execution.PullRequestNumber is { } number && execution.PullRequestUrl is { } url && execution.PublishedHeadCommit is { } head)
        {
            var source = JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot ?? "null", JsonOptions)
                ?? throw new InvalidDataException("Missing publication repository.");
            receipt = new(number, url, source.BaseCommit, head, source.Branch);
        }
        return new(execution.PublicationState ?? "not_requested", execution.PublicationAttempts, execution.PublicationFailureCode, receipt);
    }
}

public static class PublicationEndpoints
{
    public static void MapPublication(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks/{taskId:guid}/executions/{executionId:guid}")
            .WithTags("Executions").RequireAuthorization();
        group.MapGet("/publication", async (Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
            ClaimsPrincipal user, PublicationAccess publication, CancellationToken ct) =>
            await publication.ReadAsync(organizationId, projectId, taskId, executionId, user, ct)).Produces<PublicationStatus>();
        group.MapPost("/publication", async (Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
            ClaimsPrincipal user, PublicationAccess publication, CancellationToken ct) =>
        {
            await publication.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);
            return await publication.PublishAsync(executionId, ct);
        }).Produces<PublicationStatus>();
    }
}
