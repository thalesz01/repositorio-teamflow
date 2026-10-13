using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Memory;

public static class MemoryEndpoints
{
    public static void MapMemory(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/memory")
            .RequireAuthorization().WithTags("Memory");
        group.MapGet("/", async (Guid organizationId, Guid projectId, int? page, int? pageSize, bool? archived,
            ClaimsPrincipal user, ProjectAccess projects, AppDbContext db, CancellationToken ct) =>
        {
            await projects.RequireAsync(organizationId, projectId, user, ct);
            var (number, size) = Pagination.Validate(page, pageSize);
            var query = db.MemoryItems.AsNoTracking().Where(m => m.OrganizationId == organizationId && m.ProjectId == projectId
                && (archived == true ? m.ArchivedAt != null : m.ArchivedAt == null));
            var items = await query.OrderByDescending(m => m.UpdatedAt).ThenBy(m => m.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return new PageResult<MemoryDto>(items.Select(MemoryAccess.ToDto).ToList(), number, size, await query.CountAsync(ct));
        });
        group.MapPost("/{id:guid}/indexes", async (Guid organizationId, Guid projectId, Guid id, IndexInput input,
            ClaimsPrincipal user, MemoryIndexAccess index, CancellationToken ct) => await index.QueueAsync(organizationId, projectId, id, input, user, ct));
        group.MapGet("/{id:guid}/indexes", async (Guid organizationId, Guid projectId, Guid id, int? page, int? pageSize,
            ClaimsPrincipal user, MemoryAccess memory, AppDbContext db, CancellationToken ct) =>
        {
            await memory.RequireAsync(organizationId, projectId, id, user, ct);
            var (number, size) = Pagination.Validate(page, pageSize);
            var query = db.MemoryIndexes.AsNoTracking().Where(i => i.ItemId == id && i.OrganizationId == organizationId && i.ProjectId == projectId);
            var entries = await query.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return new PageResult<IndexDto>(entries.Select(MemoryIndexAccess.ToDto).ToList(), number, size, await query.CountAsync(ct));
        });
        group.MapPost("/search", async (Guid organizationId, Guid projectId, SearchInput input, ClaimsPrincipal user,
            MemorySearch search, CancellationToken ct) => await search.FindAsync(organizationId, projectId, input, user, ct));
        group.MapPost("/analyses", async (Guid organizationId, Guid projectId, AnalysisInput input, ClaimsPrincipal user,
            PlanningAccess planning, CancellationToken ct) => await planning.AnalyzeAsync(organizationId, projectId, input, user, ct));
        group.MapGet("/analyses/{id:guid}", async (Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user,
            PlanningAccess planning, CancellationToken ct) => await planning.ReadAsync(organizationId, projectId, id, user, ct));
        group.MapPost("/", async (Guid organizationId, Guid projectId, MemoryInput input, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
            await memory.CreateAsync(organizationId, projectId, input, user, ct));
        group.MapPost("/from-execution/{executionId:guid}", async (Guid organizationId, Guid projectId, Guid executionId,
            ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) => await memory.FromExecutionAsync(organizationId, projectId, executionId, user, ct));
        group.MapGet("/{id:guid}", async (Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
            MemoryAccess.Detail(await memory.RequireAsync(organizationId, projectId, id, user, ct)));
        group.MapPut("/{id:guid}", async (Guid organizationId, Guid projectId, Guid id, MemoryUpdate input, ClaimsPrincipal user,
            MemoryAccess memory, CancellationToken ct) => await memory.UpdateAsync(organizationId, projectId, id, input, user, ct));
        group.MapDelete("/{id:guid}", async (Guid organizationId, Guid projectId, Guid id, Guid version, ClaimsPrincipal user,
            MemoryAccess memory, CancellationToken ct) => await memory.ArchiveAsync(organizationId, projectId, id, version, false, user, ct));
        group.MapPost("/{id:guid}/restore", async (Guid organizationId, Guid projectId, Guid id, MemoryVersionInput input, ClaimsPrincipal user,
            MemoryAccess memory, CancellationToken ct) => await memory.ArchiveAsync(organizationId, projectId, id, input.Version, true, user, ct));
        group.MapGet("/{id:guid}/versions/{version:guid}", async (Guid organizationId, Guid projectId, Guid id, Guid version,
            ClaimsPrincipal user, MemoryAccess memory, AppDbContext db, CancellationToken ct) =>
        {
            await memory.RequireAsync(organizationId, projectId, id, user, ct);
            var revision = await db.MemoryRevisions.AsNoTracking().SingleOrDefaultAsync(r => r.ItemId == id && r.Version == version
                && r.OrganizationId == organizationId && r.ProjectId == projectId, ct)
                ?? throw new ApiException(404, "memory_version_not_found", "Source version not found.");
            return new MemoryVersionDto(revision.Version, revision.Title, revision.Content, revision.ContentHash, revision.Status,
                revision.Archived, revision.AuthorId, revision.CreatedAt);
        });
    }
}
