using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

// These columns held lowercase text before they became enums. Existing rows must keep reading and writing the same text.
public sealed class StoredEnumTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Theory]
    [InlineData(ModelTurnState.Pending, "pending")]
    [InlineData(ModelTurnState.Completed, "completed")]
    [InlineData(ModelTurnState.Uncertain, "uncertain")]
    public void ModelTurnStateKeepsItsStoredText(ModelTurnState state, string text) => AssertStoredText(state, text);

    [Theory]
    [InlineData(BillingMode.Api, "api")]
    [InlineData(BillingMode.Cli, "cli")]
    public void BillingModeKeepsItsStoredText(BillingMode mode, string text) => AssertStoredText(mode, text);

    [Theory]
    [InlineData(TaskKind.Change, "change")]
    [InlineData(TaskKind.Analysis, "analysis")]
    public void TaskKindKeepsItsStoredText(TaskKind kind, string text) => AssertStoredText(kind, text);

    [Theory]
    [InlineData(MemberRole.Member, "member")]
    [InlineData(MemberRole.Owner, "owner")]
    public void MemberRoleKeepsItsStoredText(MemberRole role, string text) => AssertStoredText(role, text);

    [Fact]
    public async Task RowsWrittenAsTextReadBackAsEnumsAndNewRowsStoreTheSameTextAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var task = await client.CreateTaskAsync(project);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var role = await db.Database.SqlQuery<string>($"""SELECT "Role" AS "Value" FROM "Memberships" WHERE "OrganizationId" = {project.OrganizationId}""").SingleAsync();
        var kind = await db.Database.SqlQuery<string>($"""SELECT "Kind" AS "Value" FROM "WorkItems" WHERE "Id" = {task.Id}""").SingleAsync();
        Assert.Equal("owner", role);
        Assert.Equal("change", kind);

        await db.Database.ExecuteSqlAsync($"""UPDATE "WorkItems" SET "Kind" = 'analysis' WHERE "Id" = {task.Id}""");
        Assert.Equal(TaskKind.Analysis, await db.WorkItems.AsNoTracking().Where(t => t.Id == task.Id).Select(t => t.Kind).SingleAsync());
    }

    private static void AssertStoredText<TEnum>(TEnum value, string text) where TEnum : struct, Enum
    {
        var converter = new LowercaseEnumConverter<TEnum>();
        Assert.Equal(text, converter.ConvertToProvider(value));
        Assert.Equal(value, converter.ConvertFromProvider(text));
    }
}
