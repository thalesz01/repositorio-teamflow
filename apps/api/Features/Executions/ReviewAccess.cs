using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed record ReviewStatus(string State, string? HeadCommit, bool CiPassed, Guid? ApprovedBy, string? MergeCommit, bool MergeAvailable = false);

public sealed class ReviewAccess(AppDbContext db, TaskAccess tasks, RepositoryAccess repositories,
    IConfiguration configuration, TimeProvider clock, TeamFlow.Api.Features.Organizations.OrganizationAccess organizations, TeamFlow.Api.Features.Memory.MemoryAccess memory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReviewStatus> ReadAsync(Guid organizationId, Guid projectId, Guid taskId, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        var execution = await RequireAsync(id, ct);
        if (execution.OrganizationId != organizationId || execution.ProjectId != projectId || execution.TaskId != taskId)
            throw new ApiException(404, "execution_not_found", "Execution not found.");
        return Status(execution);
    }

    public async Task<ReviewStatus> ReconcileAsync(Guid id, Guid? approveBy, CancellationToken ct)
    {
        var execution = await RequireAsync(id, ct);
        if (execution.PublicationState != "published" || execution.PullRequestNumber is null || execution.PublishedHeadCommit is null)
            throw new ApiException(409, "publication_required", "A confirmed PR is required.");
        if (approveBy == execution.RequestedBy)
            throw new ApiException(409, "independent_review_required", "Another team member must review the execution.");
        if (execution.ReviewState == "merged") return Status(execution);
        var source = JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot ?? "null", JsonOptions)
            ?? throw new InvalidDataException("Missing repository snapshot.");
        if (!configuration.GetValue<bool>("Local:EnableReview"))
            throw new ApiException(503, "review_disabled", "Review is disabled.");
        if (execution.State != ExecutionState.Succeeded || execution.ResultCode != "tests_passed")
            throw new ApiException(409, "validation_required", "Review requires successful runner tests.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var version = await tasks.ReviewVersionAsync(execution.OrganizationId, execution.ProjectId, execution.TaskId, ct);
        var snapshot = await repositories.ReviewAsync(execution.OrganizationId, execution.ProjectId, source, execution.PublishedHeadCommit, timeout.Token);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({execution.TaskId.ToString()}))", ct);
        var latestId = await db.Executions.Where(e => e.TaskId == execution.TaskId && e.OrganizationId == execution.OrganizationId && e.ProjectId == execution.ProjectId)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Select(e => e.Id).FirstAsync(ct);
        if (latestId != id) throw new ApiException(409, "review_task_changed", "A newer execution superseded this review.");
        execution = await db.Executions.FromSqlInterpolated($"SELECT * FROM \"Executions\" WHERE \"Id\" = {id} FOR UPDATE").SingleAsync(ct);
        await db.Entry(execution).ReloadAsync(ct);
        if (execution.ReviewState == "merged") return Status(execution);
        if (await tasks.ReviewVersionAsync(execution.OrganizationId, execution.ProjectId, execution.TaskId, ct) != version)
            throw new ApiException(409, "review_conflict", "Review changed during the Git query; refresh before applying it.");
        var matches = snapshot.HeadCommit == execution.PublishedHeadCommit
            && (snapshot.Merged ? snapshot.MergeBaseParent : snapshot.BaseCommit) == source.BaseCommit
            && (!snapshot.Merged || GitRules.IsCommit(snapshot.MergeCommit ?? ""));
        if (approveBy is { } actor && !await organizations.IsMemberAsync(execution.OrganizationId, actor, ct))
            throw new ApiException(403, "reviewer_not_member", "Reviewer no longer belongs to this organization.");
        execution.ReviewHeadCommit = snapshot.HeadCommit;
        execution.ReviewCiPassed = snapshot.CiPassed;
        execution.ReviewCheckedAt = clock.GetUtcNow();
        if (!matches)
        {
            execution.ReviewApprovedBy = null;
            execution.ReviewApprovedHead = null;
            execution.ReviewState = "revalidation_required";
            await tasks.ApplyReviewStageAsync(execution.OrganizationId, execution.ProjectId, execution.TaskId, TaskStage.Review, ct);
        }
        else
        {
            if (approveBy is { } reviewer)
            {
                if (snapshot.Merged || !snapshot.CiPassed) throw new ApiException(409, "ci_required", "Approve before merge with successful configured CI.");
                execution.ReviewApprovedBy = reviewer;
                execution.ReviewApprovedHead = snapshot.HeadCommit;
            }
            var approved = execution.ReviewApprovedBy is { } recordedReviewer && execution.ReviewApprovedHead == snapshot.HeadCommit
                && await organizations.IsMemberAsync(execution.OrganizationId, recordedReviewer, ct);
            execution.ReviewState = snapshot.Merged ? approved && snapshot.CiPassed ? "merged" : "merge_policy_unverified"
                : !snapshot.CiPassed ? "ci_pending" : approved ? "awaiting_merge" : "review_required";
            if (execution.ReviewState == "merged") execution.ConfirmedMergeCommit = snapshot.MergeCommit;
            var stage = execution.ReviewState == "merged" ? TaskStage.Done : execution.ReviewState == "awaiting_merge" ? TaskStage.AwaitingMerge : TaskStage.Review;
            if (await tasks.ApplyReviewStageAsync(execution.OrganizationId, execution.ProjectId, execution.TaskId, stage, ct) != 1)
                throw new ApiException(409, "review_task_changed", "A newer execution or task stage superseded this review.");
        }
        await db.SaveChangesAsync(ct);
        if (execution.ReviewState == "merged")
            await memory.ExtractAsync(execution.OrganizationId, execution.ProjectId, id, execution.ReviewApprovedBy ?? execution.RequestedBy, ct);
        await transaction.CommitAsync(ct);
        return Status(execution);
    }

    /// <summary>Fast-forwards the base branch to the approved head, then confirms the merge through the normal review check.</summary>
    public async Task<ReviewStatus> MergeAsync(Guid id, Guid requestedBy, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Local:EnableMerge"))
            throw new ApiException(503, "merge_disabled", "Merging from the app is disabled on this server.");
        // Refresh first: approval, head, base and parent must still hold at the moment of the merge.
        var refreshed = await ReconcileAsync(id, null, ct);
        if (refreshed.State == "merged") return refreshed;
        var execution = await RequireAsync(id, ct);
        if (refreshed.State != "awaiting_merge" || execution.ReviewApprovedBy is null || execution.PublishedHeadCommit is not { } head
            || execution.ReviewApprovedHead != head || !await organizations.IsMemberAsync(execution.OrganizationId, execution.ReviewApprovedBy.Value, ct))
            throw new ApiException(409, "approval_required", "An independent approval of the current commit is required before merging.");
        var source = JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot ?? "null", JsonOptions)
            ?? throw new InvalidDataException("Missing repository snapshot.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await repositories.MergeAsync(execution.OrganizationId, execution.ProjectId, source, head, timeout.Token);
        await db.Executions.Where(e => e.Id == id).ExecuteUpdateAsync(set => set
            .SetProperty(e => e.MergeRequestedBy, requestedBy).SetProperty(e => e.MergeRequestedAt, clock.GetUtcNow()), ct);
        return await ReconcileAsync(id, null, ct);
    }

    private async Task<Execution> RequireAsync(Guid id, CancellationToken ct) =>
        await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new ApiException(404, "execution_not_found", "Execution not found.");
    private ReviewStatus Status(Execution execution) => new(execution.ReviewState ?? "not_checked", execution.ReviewHeadCommit,
        execution.ReviewCiPassed, execution.ReviewApprovedBy, execution.ConfirmedMergeCommit,
        execution.ReviewState == "awaiting_merge" && configuration.GetValue<bool>("Local:EnableMerge"));
}

public static class ReviewEndpoints
{
    public static void MapReviews(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks/{taskId:guid}/executions/{executionId:guid}")
            .RequireAuthorization().WithTags("Reviews");
        group.MapGet("/review", async (Guid organizationId, Guid projectId, Guid taskId, Guid executionId, ClaimsPrincipal user,
            ReviewAccess reviews, CancellationToken ct) => await reviews.ReadAsync(organizationId, projectId, taskId, executionId, user, ct));
        foreach (var command in new[] { "approve", "refresh", "merge" })
        {
            var approve = command == "approve";
            group.MapPost("/review/" + command, async (Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
                ClaimsPrincipal user, ReviewAccess reviews, CancellationToken ct) =>
            {
                await reviews.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);
                if (command == "merge") return await reviews.MergeAsync(executionId, AuthEndpoints.UserId(user), ct);
                return await reviews.ReconcileAsync(executionId, approve ? AuthEndpoints.UserId(user) : null, ct);
            });
        }
    }
}
