namespace TS_DJ.Core.Models;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] {Level}: {Message}";
}
