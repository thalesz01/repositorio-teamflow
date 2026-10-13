using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public static class ApiClient
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static async Task<ProjectDto> CreateProjectAsync(this HttpClient client)
    {
        using var organizationResponse = await client.SendAsync(HttpMethod.Post, "/api/v1/organizations/", new OrganizationInput("Test team"));
        var organization = await organizationResponse.ReadAsync<OrganizationDto>();
        using var projectResponse = await client.SendAsync(HttpMethod.Post, $"/api/v1/organizations/{organization.Id}/projects/", new ProjectInput("Test project"));
        return await projectResponse.ReadAsync<ProjectDto>();
    }

    // Production creates the Codex binding through the machine endpoint, which needs the CLI. Tests that
    // only need a scoped profile (optionally priced to exercise budgets) insert the same record directly.
    public static async Task<ProfileDto> SeedProfileAsync(this IServiceProvider services, ProjectDto project, string name = "Fixture",
        decimal inputUsdPerMillionTokens = 1m, decimal outputUsdPerMillionTokens = 1m)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = new AgentProfile
        {
            Id = Guid.NewGuid(),
            OrganizationId = project.OrganizationId,
            ProjectId = project.Id,
            Provider = "codex",
            Name = name,
            Model = "default",
            MaxInputBytes = 32768,
            MaxOutputTokens = 8192,
            TimeoutSeconds = 120,
            InputUsdPerMillionTokens = inputUsdPerMillionTokens,
            OutputUsdPerMillionTokens = outputUsdPerMillionTokens
        };
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();
        return ProfileEndpoints.ToDto(profile);
    }

    public static string TasksPath(ProjectDto project) => $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/tasks";

    public static async Task<TaskDto> CreateTaskAsync(this HttpClient client, ProjectDto project, Guid[]? dependencies = null)
    {
        using var response = await client.SendAsync(HttpMethod.Post, TasksPath(project) + "/",
            new TaskInput("Fix calculation", "Correct rounding behavior.", ["Result is rounded correctly."], null, dependencies ?? []));
        return await response.ReadAsync<TaskDto>();
    }

    public static async Task<SessionDto> RegisterAsync(this HttpClient client)
    {
        using var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/register",
            new Credentials($"{Guid.NewGuid():N}@example.test", "Local-Test-Password42!"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionDto>() ?? throw new InvalidOperationException("Missing session.");
    }

    public static async Task<HttpResponseMessage> SendAsync<T>(this HttpClient client, HttpMethod method, string path, T body, string? idempotencyKey = null)
    {
        var csrf = await client.GetFromJsonAsync<CsrfDto>("/api/v1/auth/csrf");
        Assert.NotNull(csrf);
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: JsonOptions) };
        request.Headers.Add("X-CSRF-Token", csrf.Token);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions) ?? throw new InvalidOperationException("Missing response body.");
    }
}
