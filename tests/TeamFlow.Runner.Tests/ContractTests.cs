using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class ContractTests
{
    [Fact]
    public void ContractRejectsForeignScopeUnsupportedVersionAndExpiredLease()
    {
        var organization = Guid.NewGuid();
        var project = Guid.NewGuid();
        var worker = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var message = new ExecutionMessage(1, Guid.NewGuid(), Guid.NewGuid(), organization, project, worker, Guid.NewGuid(),
            now.AddSeconds(30), now.AddMinutes(10), 1, new("Task", "Description", ["Acceptance"], Guid.NewGuid()), new(900, 3, 50, 5m), null, null);
        RunnerClient.ValidateMessage(message, organization, project, worker, now);
        Assert.Throws<InvalidDataException>(() => RunnerClient.ValidateMessage(message with { SchemaVersion = 2 }, organization, project, worker, now));
        Assert.Throws<InvalidDataException>(() => RunnerClient.ValidateMessage(message, organization, Guid.NewGuid(), worker, now));
        Assert.Throws<InvalidDataException>(() => RunnerClient.ValidateMessage(message with { LeaseExpiresAt = now.AddSeconds(-1) }, organization, project, worker, now));
        Assert.Throws<InvalidDataException>(() => RunnerClient.ValidateMessage(message with { Limits = new(900, 3, 201, 5) }, organization, project, worker, now));
    }

    [Fact]
    public void ClientRejectsInsecureRemoteOriginsAndEmbeddedCredentials()
    {
        Assert.Throws<ArgumentException>(() => new RunnerClient(new Uri("http://example.test"), "token", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new RunnerClient(new Uri("https://user:secret@example.test"), "token", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }
}
