namespace TeamFlow.Runner;

/// <summary>
/// Small rolling log next to the controller file. Nothing else records why a local runner stopped, so failures
/// must not disappear. It holds event names and exception text only: never prompts, file contents or tokens.
/// </summary>
public sealed class RunnerLog(string? path)
{
    private const long MaxBytes = 512 * 1024;
    private readonly object _gate = new();

    public void Write(string message)
    {
        if (path is null) return;
        var line = message.ReplaceLineEndings(" ");
        if (line.Length > 600) line = line[..600];
        lock (_gate)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {line}{Environment.NewLine}");
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { /* logging must never stop the runner */ }
        }
    }

    public void Failure(string context, Exception error) => Write($"{context}: {error.GetType().Name}: {error.Message}");
}
