using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class CheckpointContractTests
{
    [Fact]
    public async Task CheckpointAdapterSendsPortableContextAndRejectsFalseAcknowledgementAsync()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var server = new HttpListener();
        var origin = new Uri($"http://127.0.0.1:{port}/");
        server.Prefixes.Add(origin.AbsoluteUri);
        server.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;
        var message = new ExecutionMessage(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), now.AddSeconds(30), now.AddMinutes(10), 1,
            new("Task", "Description", ["Acceptance"], Guid.NewGuid()), new(900, 3, 50, 5m), null, null);
        var context = new PortableContext(1, message.ExecutionId, "Task\nDescription", ["Acceptance"], new string('a', 40), [], []);
        using var client = new RunnerClient(origin, "fixture-token", message.OrganizationId, message.ProjectId, message.WorkerId);
        var serving = ServeAsync();
        await new RemoteAgentCheckpoint(client, message).SaveAsync(context, timeout.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.SaveCheckpointAsync(message, context, timeout.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.SaveCheckpointAsync(message,
            context with { ExecutionId = Guid.NewGuid() }, timeout.Token));
        await serving;

        async Task ServeAsync()
        {
            for (var i = 0; i < 2; i++)
            {
                var request = await server.GetContextAsync().WaitAsync(timeout.Token);
                Assert.Equal("PUT", request.Request.HttpMethod);
                Assert.Equal($"/internal/v1/executions/{message.ExecutionId:D}/checkpoint/", request.Request.Url?.AbsolutePath);
                Assert.Equal("Bearer fixture-token", request.Request.Headers["Authorization"]);
                using var reader = new StreamReader(request.Request.InputStream);
                var body = await reader.ReadToEndAsync(timeout.Token);
                Assert.DoesNotContain("fixture-token", body);
                using var document = JsonDocument.Parse(body);
                Assert.Equal(message.LeaseToken, document.RootElement.GetProperty("leaseToken").GetGuid());
                var sent = document.RootElement.GetProperty("context");
                Assert.Equal(context.Objective, sent.GetProperty("objective").GetString());
                var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(context, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new RemoteCheckpointReply(message.Attempt, i, hash, now, context),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                request.Response.StatusCode = 200;
                request.Response.ContentType = "application/json";
                request.Response.ContentLength64 = bytes.Length;
                await request.Response.OutputStream.WriteAsync(bytes, timeout.Token);
                request.Response.Close();
            }
        }
    }
}
