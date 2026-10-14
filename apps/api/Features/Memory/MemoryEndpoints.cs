using System.Security.Claims;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Memory;

public static class MemoryEndpoints
{
    public static void MapMemory(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/memory")
            .RequireAuthorization().WithTags("Memory");
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPost("/search", SearchAsync);
        group.MapPost("/analyses", AnalyzeAsync);
        group.MapGet("/analyses/{id:guid}", ReadAnalysisAsync);
        group.MapPost("/from-execution/{executionId:guid}", FromExecutionAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", ArchiveAsync);
        group.MapPost("/{id:guid}/restore", RestoreAsync);
        group.MapGet("/{id:guid}/versions/{version:guid}", VersionAsync);
        group.MapPost("/{id:guid}/indexes", QueueIndexAsync);
        group.MapGet("/{id:guid}/indexes", ListIndexesAsync);
    }

    private static async Task<PageResult<MemoryDto>> ListAsync(Guid organizationId, Guid projectId, int? page, int? pageSize, bool? archived,
        ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
        await memory.ListAsync(organizationId, projectId, page, pageSize, archived, user, ct);

    private static async Task<MemoryDetail> CreateAsync(Guid organizationId, Guid projectId, MemoryInput input, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
        await memory.CreateAsync(organizationId, projectId, input, user, ct);

    private static async Task<ContextBundle> SearchAsync(Guid organizationId, Guid projectId, SearchInput input, ClaimsPrincipal user, MemorySearch search, CancellationToken ct) =>
        await search.FindAsync(organizationId, projectId, input, user, ct);

    private static async Task<AnalysisDto> AnalyzeAsync(Guid organizationId, Guid projectId, AnalysisInput input, ClaimsPrincipal user, PlanningAccess planning, CancellationToken ct) =>
        await planning.AnalyzeAsync(organizationId, projectId, input, user, ct);

    private static async Task<AnalysisDto> ReadAnalysisAsync(Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, PlanningAccess planning, CancellationToken ct) =>
        await planning.ReadAsync(organizationId, projectId, id, user, ct);

    private static async Task<MemoryDetail> FromExecutionAsync(Guid organizationId, Guid projectId, Guid executionId, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
        await memory.FromExecutionAsync(organizationId, projectId, executionId, user, ct);

    private static async Task<MemoryDetail> GetAsync(Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
        MemoryAccess.Detail(await memory.RequireAsync(organizationId, projectId, id, user, ct));

    private static async Task<MemoryDetail> UpdateAsync(Guid organizationId, Guid projectId, Guid id, MemoryUpdate input, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
        await memory.UpdateAsync(organizationId, projectId, id, input, user, ct);

    private static async Task<MemoryDetail> ArchiveAsync(Guid organizationId, Guid projectId, Guid id, Guid version, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
        await memory.ArchiveAsync(organizationId, projectId, id, version, false, user, ct);

    private static async Task<MemoryDetail> RestoreAsync(Guid organizationId, Guid projectId, Guid id, MemoryVersionInput input, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
        await memory.ArchiveAsync(organizationId, projectId, id, input.Version, true, user, ct);

    private static async Task<MemoryVersionDto> VersionAsync(Guid organizationId, Guid projectId, Guid id, Guid version, ClaimsPrincipal user, MemoryAccess memory, CancellationToken ct) =>
        await memory.VersionAsync(organizationId, projectId, id, version, user, ct);

    private static async Task<IndexDto> QueueIndexAsync(Guid organizationId, Guid projectId, Guid id, IndexInput input, ClaimsPrincipal user, MemoryIndexAccess index, CancellationToken ct) =>
        await index.QueueAsync(organizationId, projectId, id, input, user, ct);

    private static async Task<PageResult<IndexDto>> ListIndexesAsync(Guid organizationId, Guid projectId, Guid id, int? page, int? pageSize, ClaimsPrincipal user, MemoryIndexAccess index, CancellationToken ct) =>
        await index.ListAsync(organizationId, projectId, id, page, pageSize, user, ct);
}
