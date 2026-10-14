using System.Text.Json;
using System.Text.Json.Nodes;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using Xunit;

namespace TeamFlow.Api.Tests;

// ADR 0016: the model sees a smaller view of the context; the persisted checkpoint is untouched.
public sealed class PromptContextTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static object Entry(int step, string tool, string? path, string detail, bool succeeded = true) =>
        new { step, tool, path, succeeded, detail, hash = path is null ? null : new string('a', 64) };

    private static string Context(params object[] evidence) =>
        JsonSerializer.Serialize(new { schemaVersion = 1, objective = "Calcular o frete", acceptanceCriteria = new[] { "Passa" }, evidence, pending = Array.Empty<string>() });

    private static string[] Details(string rendered) =>
        JsonNode.Parse(rendered)!["evidence"]!.AsArray().Select(e => e!["detail"]!.GetValue<string>()).ToArray();

    private static string TestDetail(string output, string error) => JsonSerializer.Serialize(new
    {
        ExitCode = 1,
        TimedOut = false,
        OutputTruncated = false,
        StandardOutput = output,
        StandardError = error,
        Executable = "dotnet",
        Arguments = new[] { "test", "--no-restore" },
        Timeout = "00:05:00",
        ReadOnlyWorkspace = true,
        environment = new { image = "sdk", network = "none" }
    });

    [Fact]
    public void TestEvidenceKeepsTheOutcomeAndDropsTheRepeatedCommandAndEnvironment()
    {
        var context = Context(Entry(1, "test", null, TestDetail(new string('o', 2500), "falhou: esperado 10, obtido 9"), false),
            Entry(2, "write", "a.cs", "file_updated"), Entry(3, "test", null, TestDetail(new string('x', 2500), "falhou de novo"), false));
        var details = Details(PromptContext.Render(context));
        Assert.StartsWith("exit 1", details[0]);
        Assert.DoesNotContain("environment", details[0]); Assert.DoesNotContain("--no-restore", details[2]);
        Assert.Contains("falhou de novo", details[2]);
        // Only the newest diagnostic is what the model must act on, so it keeps more text than the older one.
        Assert.True(details[0].Length < 700 && details[2].Length > 2000);
    }

    [Fact]
    public void ARepeatedReadKeepsOnlyTheLatestContent()
    {
        var big = new string('c', 3000);
        var context = Context(Entry(1, "read", "a.cs", big), Entry(2, "write", "a.cs", "file_updated"), Entry(3, "read", "a.cs", big + "!"), Entry(4, "read", "b.cs", big));
        var rendered = Details(PromptContext.Render(context));
        Assert.Contains("superseded", rendered[0]); Assert.Equal(big + "!", rendered[2]); Assert.Equal(big, rendered[3]);
        Assert.Equal("file_updated", rendered[1]);
    }

    [Fact]
    public void OldestContentIsOmittedOnlyOverTheBudgetAndTheNewestStays()
    {
        var reads = Enumerable.Range(1, 6).Select(i => Entry(i, "read", $"f{i}.cs", new string((char)('a' + i), 5000))).ToArray();
        var rendered = PromptContext.Render(Context(reads), high: 20_000, low: 10_000);
        var details = Details(rendered);
        Assert.Contains("omitted", details[0]); Assert.Equal(new string((char)('a' + 6), 5000), details[5]);
        Assert.Contains(new string('a', 64), rendered);
        Assert.DoesNotContain("omitted", string.Concat(Details(PromptContext.Render(Context(reads[..3]), 20_000, 10_000))));
    }

    [Fact]
    public void TheTextChangesOnlyWhenTheBudgetIsReachedSoTheProviderCacheKeepsMatching()
    {
        var reads = Enumerable.Range(1, 12).Select(i => Entry(i, "read", $"f{i}.cs", new string((char)('a' + i), 5000))).ToArray();
        string[]? before = null;
        var rewrites = 0;
        for (var n = 1; n <= reads.Length; n++)
        {
            var now = Details(PromptContext.Render(Context(reads[..n]), 20_000, 10_000));
            if (before is not null && !before.SequenceEqual(now[..before.Length])) rewrites++;
            before = now;
        }
        Assert.InRange(rewrites, 1, 4);
    }

    [Fact]
    public void KeepsReadableTextAndNeverBreaksATurnOnInvalidContext()
    {
        var context = Context(Entry(1, "read", "Ação.cs", "List<string> x = a && b; // preço 'R$'"));
        var rendered = PromptContext.Render(context);
        Assert.Contains("List<string>", rendered); Assert.Contains("preço", rendered); Assert.DoesNotContain("\\u", rendered);
        Assert.Equal("not json", PromptContext.Render("not json"));
        Assert.Equal("{\"a\":1}", PromptContext.Render("{\"a\":1}"));
    }

    [Fact]
    public void AMultiStepRunSendsClearlyLessThanTheStoredContextAtEveryStep()
    {
        string Source(char c) => string.Concat(Enumerable.Repeat($"public decimal Rate{c}(Order order) {{ return order.Total * 0.0{c}m; }} // preço\n", 130));
        var stdout = string.Concat(Enumerable.Repeat("Passed Tests.Shipping.Rate\n", 120));
        var run = new object[]
        {
            Entry(1, "read", "A.cs", Source('1')), Entry(2, "read", "B.cs", Source('2')), Entry(3, "read", "C.cs", Source('3')),
            Entry(4, "write", "A.cs", "file_updated"), Entry(5, "test", null, TestDetail(stdout, "A.cs(10): error CS1002"), false),
            Entry(6, "read", "A.cs", Source('4')), Entry(7, "write", "A.cs", "file_updated"), Entry(8, "test", null, TestDetail(stdout, "B.cs(3): assert"), false),
            Entry(9, "write", "B.cs", "file_updated"), Entry(10, "test", null, TestDetail(stdout, ""))
        };
        long stored = 0, sent = 0;
        for (var n = 1; n <= run.Length; n++)
        {
            var context = Context(run[..n]);
            stored += context.Length; sent += PromptContext.Render(context).Length;
        }
        output.WriteLine($"stored context over 10 steps: {stored} chars; sent: {sent} chars ({100 - sent * 100 / stored}% less)");
        Assert.True(sent < stored * 0.8, $"sent {sent} of {stored}");
    }

    [Fact]
    public void CliUsageSumsCodexTurnsClaudeCacheAndNeverTurnsMissingFiguresIntoZero()
    {
        var codex = new CliUsage.CodexCollector();
        codex.Add("{\"type\":\"thread.started\"}"); codex.Add("not json turn.completed");
        Assert.Null(codex.Total.Input);
        codex.Add("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1000,\"cached_input_tokens\":800,\"output_tokens\":20}}");
        codex.Add("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":500,\"cached_input_tokens\":0,\"output_tokens\":5}}");
        Assert.Equal(new CliUsage(1500, 800, 25), codex.Total);
        codex.Add("{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":10,\"output_tokens\":1}}");
        Assert.Equal(new CliUsage(1510, null, 26), codex.Total);

        using var claude = JsonDocument.Parse("{\"usage\":{\"input_tokens\":2,\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":500,\"output_tokens\":4}}");
        Assert.Equal(new CliUsage(602, 500, 4), CliUsage.FromClaude(claude.RootElement));
        using var none = JsonDocument.Parse("{\"result\":\"x\"}");
        Assert.Equal(new CliUsage(null, null, null), CliUsage.FromClaude(none.RootElement));
    }
}
