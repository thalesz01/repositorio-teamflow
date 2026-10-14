using TeamFlow.Api.Features.Executions;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class TestEvidenceTests
{
    [Fact]
    public void LatestTestMustPassAndTimeoutsCannotBeRecovered()
    {
        var context = new CheckpointContext(1, Guid.NewGuid(), "Fix", ["Build"], new string('a', 40),
            [new(1, "write", "answer.txt", true, "written"), new(2, "test", null, true, "passed"),
             new(3, "test", null, false, "{\"ExitCode\":1,\"TimedOut\":false,\"OutputTruncated\":false}")], []);
        Assert.False(TeamFlow.Api.Features.Executions.TestEvidence.IsValidated(context));
        Assert.True(TeamFlow.Api.Features.Executions.TestEvidence.IsValidated(context with
        { Evidence = [.. context.Evidence, new(4, "test", null, true, "passed")] }));
        Assert.False(TeamFlow.Api.Features.Executions.TestEvidence.IsRecoverable(context.Evidence[2] with
        { Detail = "{\"ExitCode\":1,\"TimedOut\":true,\"OutputTruncated\":false}" }));
        Assert.False(TeamFlow.Api.Features.Executions.TestEvidence.IsRecoverable(context.Evidence[2] with { Tool = "rejected" }));
        Assert.False(TeamFlow.Api.Features.Executions.TestEvidence.IsRecoverable(context.Evidence[2] with { Detail = "unknown" }));
    }

    [Fact]
    public void DiagnosticsAreBoundedAndCredentialsAreRemoved()
    {
        var output = TestOutput.Sanitize("error CS1002; password=supersecret; Bearer abcdefghijklmnopqrstuvwxyz\n" + new string('a', 6000));
        Assert.Contains("CS1002", output);
        Assert.DoesNotContain("supersecret", output);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", output);
        Assert.True(output.Length < 3100);
        Assert.True(TeamFlow.Runner.TestEvidence.SameValidation("{\"ExitCode\":0,\"StandardOutput\":\"first time\"}", "{\"ExitCode\":0,\"StandardOutput\":\"second time\"}"));
        Assert.False(TeamFlow.Runner.TestEvidence.SameValidation("{\"ExitCode\":0}", "{\"ExitCode\":1}"));
    }
}
