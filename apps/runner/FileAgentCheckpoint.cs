using System.Text.Json;

namespace TeamFlow.Runner;

// Controller-owned storage must be outside the repository mount exposed to the sandbox.
public sealed class FileAgentCheckpoint(ExecutionWorkspace controller, Guid executionId) : IAgentCheckpoint
{
    public async Task SaveAsync(PortableContext context, CancellationToken ct)
    {
        if (executionId == Guid.Empty || context.ExecutionId != executionId)
            throw new ArgumentException("Checkpoint execution scope mismatch.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(context, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Checkpoint exceeds storage limit.");
        var temporary = controller.Resolve($"{executionId:N}.tmp");
        var destination = controller.Resolve($"{executionId:N}.json");
        await File.WriteAllBytesAsync(temporary, bytes, ct);
        ct.ThrowIfCancellationRequested();
        File.Move(temporary, destination, overwrite: true);
    }
}
