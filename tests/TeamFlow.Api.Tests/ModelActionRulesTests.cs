using TeamFlow.Api.Features.Executions;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class ModelActionRulesTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly AgentToolPolicy Policy = new(["src/a.cs", "docs/readme.md"], ["src/a.cs"], ["src/new/**"]);

    [Fact]
    public void WellFormedPolicyIsAccepted() => Assert.True(ModelActionRules.IsValidPolicy(Policy));

    [Fact]
    public void MissingListsAreRejected()
    {
        Assert.False(ModelActionRules.IsValidPolicy(null));
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy(null!, [])));
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy([], null!)));
    }

    [Theory]
    [InlineData("C:/secrets.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\system32")]
    [InlineData("src/.git/config")]
    [InlineData(".env")]
    [InlineData("src//a.cs")]
    [InlineData("   ")]
    public void UnsafePolicyPathsAreRejectedInEveryList(string path)
    {
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy([path], [])));
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy([], [path])));
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy([], [], [path])));
    }

    [Fact]
    public void PolicySizeLimitsAreEnforced()
    {
        var many = Enumerable.Range(0, 101).Select(i => $"f{i}.cs").ToArray();
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy(many, [])));
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy([], many)));
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy([], [], Enumerable.Range(0, 21).Select(i => $"d{i}/**").ToArray())));
        Assert.False(ModelActionRules.IsValidPolicy(new AgentToolPolicy([new string('x', 241)], [])));
    }

    [Theory]
    [InlineData("finish", true)]
    [InlineData("test", true)]
    [InlineData("shell", false)]
    [InlineData("", false)]
    public void OnlyKnownToolsAreAccepted(string tool, bool valid) =>
        Assert.Equal(valid, ModelActionRules.IsValidAction(new ModelAction(tool), Policy, analysis: false));

    [Fact]
    public void MissingActionIsRejected() => Assert.False(ModelActionRules.IsValidAction(null, Policy, analysis: false));

    [Fact]
    public void AnalysisNeverWritesOrTests()
    {
        var write = new ModelAction("write", "src/a.cs", "x", Hash);
        Assert.True(ModelActionRules.IsValidAction(write, Policy, analysis: false));
        Assert.False(ModelActionRules.IsValidAction(write, Policy, analysis: true));
        Assert.False(ModelActionRules.IsValidAction(new ModelAction("test"), Policy, analysis: true));
        Assert.True(ModelActionRules.IsValidAction(new ModelAction("read", "src/a.cs"), Policy, analysis: true));
        Assert.True(ModelActionRules.IsValidAction(new ModelAction("finish", Content: "answer"), Policy, analysis: true));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("a", false)]
    [InlineData("ab", true)]
    public void SearchNeedsAReasonableLiteral(string? content, bool valid) =>
        Assert.Equal(valid, ModelActionRules.IsValidAction(new ModelAction("search", Content: content), Policy, analysis: false));

    [Fact]
    public void SearchLiteralIsLimitedTo200Characters()
    {
        Assert.True(ModelActionRules.IsValidAction(new ModelAction("search", Content: new string('x', 200)), Policy, analysis: false));
        Assert.False(ModelActionRules.IsValidAction(new ModelAction("search", Content: new string('x', 201)), Policy, analysis: false));
    }

    [Theory]
    [InlineData("docs/readme.md", true)]
    [InlineData("src/new/deep/file.cs", true)]
    [InlineData("src/other.cs", false)]
    [InlineData(null, false)]
    public void ReadIsLimitedToListedPathsOrCreatePatterns(string? path, bool valid) =>
        Assert.Equal(valid, ModelActionRules.IsValidAction(new ModelAction("read", path), Policy, analysis: false));

    [Theory]
    [InlineData("src/a.cs", true)]
    [InlineData("docs/readme.md", false)]
    [InlineData("src/new/file.cs", true)]
    [InlineData(null, false)]
    public void WriteIsLimitedToEditablePathsOrCreatePatterns(string? path, bool valid) =>
        Assert.Equal(valid, ModelActionRules.IsValidAction(new ModelAction("write", path, "x", Hash), Policy, analysis: false));

    [Fact]
    public void WriteNeedsContentWithinTheLimitAndAWellFormedHash()
    {
        bool Valid(string? content, string? hash) => ModelActionRules.IsValidAction(new ModelAction("write", "src/a.cs", content, hash), Policy, analysis: false);
        Assert.True(Valid("x", Hash));
        Assert.True(Valid("x", "missing"));
        Assert.True(Valid(new string('x', 16384), Hash));
        Assert.False(Valid(new string('x', 16385), Hash));
        Assert.False(Valid(new string('é', 8193), Hash));
        Assert.False(Valid(null, Hash));
        Assert.False(Valid("x", null));
        Assert.False(Valid("x", new string('a', 63)));
        Assert.False(Valid("x", new string('z', 64)));
    }
}
