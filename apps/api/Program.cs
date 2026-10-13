using TeamFlow.Api.Features.Memory;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Executions;
using System.Text.Json;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Projects;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeamFlow.Api;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

var provisionRunner = args is ["--provision-local-runner", _, _];
var retryExecution = args is ["--retry-local-execution", _];
var builder = WebApplication.CreateBuilder(provisionRunner || retryExecution ? [] : args);
if (provisionRunner || retryExecution) builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
}
if (builder.Environment.IsProduction())
    throw new InvalidOperationException("Production deployment is blocked until the approved sandbox and environment secret protection are implemented.");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 128 * 1024);

builder.Services.AddOptions<ServerOptions>()
    .BindConfiguration(ServerOptions.SectionName)
    .Validate(options => ServerOptions.IsValidUrl(options.Url), "Server:Url must be an absolute HTTP(S) origin.")
    .ValidateOnStart();
var connectionString = builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings:Database is required.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("code", $"http_{context.HttpContext.Response.StatusCode}");
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.AddTeamFlowAuth();
builder.Services.AddAuthentication().AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, RunnerAuthentication>(RunnerAuthentication.SchemeName, _ => { });
builder.Services.AddAuthorization(options => options.AddPolicy("runner", policy => policy.AddAuthenticationSchemes(RunnerAuthentication.SchemeName).RequireAuthenticatedUser()));
builder.Services.AddScoped<OrganizationAccess>();
builder.Services.AddScoped<ProjectAccess>();
builder.Services.AddScoped<TaskAccess>();
builder.Services.AddScoped<ExecutionScheduler>();
builder.Services.AddScoped<LeaseAccess>();
builder.Services.AddScoped<CheckpointAccess>();
builder.Services.AddScoped<ModelTurnAccess>();
builder.Services.AddScoped<RecoveryAccess>();
builder.Services.AddScoped<ExecutionTransfer>();
builder.Services.AddScoped<ProgressAccess>();
builder.Services.AddScoped<PublicationAccess>();
builder.Services.AddScoped<ReviewAccess>();
builder.Services.AddScoped<MemoryAccess>();
builder.Services.AddScoped<MemoryIndexAccess>();
builder.Services.AddScoped<MemorySearch>();
builder.Services.AddScoped<PlanningAccess>();
builder.Services.AddHostedService<MemoryIndexWorker>();
builder.Services.AddScoped<ExecutionKnowledge>();
builder.Services.AddHostedService<PublicationWorker>();
builder.Services.AddScoped<ExecutionResultAccess>();
builder.Services.AddScoped<RunnerAccess>();
builder.Services.AddScoped<RepositoryAccess>();
builder.Services.AddScoped<ProfileAccess>();
builder.Services.AddScoped<CodexMachine>();
builder.Services.AddScoped<LocalRepository>();
builder.Services.AddScoped<LocalGit>();
builder.Services.AddScoped<ITextModel, CodexTextModel>();
builder.Services.AddScoped<IGitPublisher>(services => services.GetRequiredService<LocalGit>());
builder.Services.AddScoped<IGitReview>(services => services.GetRequiredService<LocalGit>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();
if (provisionRunner)
{
    using var scope = app.Services.CreateScope();
    var issued = await LocalRunnerProvisioning.IssueAsync(scope.ServiceProvider, app.Environment,
        Guid.Parse(args[1]), Guid.Parse(args[2]), CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(issued, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return;
}
if (retryExecution)
{
    using var scope = app.Services.CreateScope();
    var execution = await LocalRunnerProvisioning.RetryAsync(scope.ServiceProvider, app.Environment, Guid.Parse(args[1]), CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(execution, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return;
}
app.Urls.Add(app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.Url);
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/internal"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseTeamFlowAuth();
app.MapAuth();
app.MapOrganizations();
app.MapProjects();
app.MapTasks();
app.MapExecutions();
app.MapProgress();
app.MapPublication();
app.MapReviews();
app.MapMemory();
app.MapRunners();
app.MapCheckpoints();
app.MapAgentExecutions();
app.MapRepositories();
app.MapProfiles();
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.Run();

public partial class Program;
