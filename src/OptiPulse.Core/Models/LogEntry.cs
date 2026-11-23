namespace OptiPulse.Core.Models;

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; } = LogLevel.Info;
    public string Source { get; init; } = "System";
    public string Message { get; init; } = string.Empty;

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss");
}
