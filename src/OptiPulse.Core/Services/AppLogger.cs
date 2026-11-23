using OptiPulse.Core.Models;

namespace OptiPulse.Core.Services;

public sealed class AppLogger
{
    private readonly object _sync = new();
    private readonly List<LogEntry> _entries = new();
    private readonly string _logDirectory;

    public event Action<LogEntry>? EntryAdded;

    public AppLogger(string? customDirectory = null)
    {
        _logDirectory = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OptiPulse",
            "logs");

        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch
        {
            // Ignore directory creation failures in restricted environments
        }
    }

    public string LogDirectory => _logDirectory;

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_sync)
        {
            return _entries.ToList();
        }
    }

    public void Info(string source, string message) => Write(LogLevel.Info, source, message);
    public void Success(string source, string message) => Write(LogLevel.Success, source, message);
    public void Warning(string source, string message) => Write(LogLevel.Warning, source, message);
    public void Error(string source, string message) => Write(LogLevel.Error, source, message);

    public void Write(LogLevel level, string source, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,
            Message = message
        };

        lock (_sync)
        {
            _entries.Add(entry);
            if (_entries.Count > 2000)
            {
                _entries.RemoveRange(0, _entries.Count - 2000);
            }

            try
            {
                var logFile = Path.Combine(_logDirectory, $"optipulse_{DateTime.Now:yyyy-MM-dd}.log");
                var utf8Bom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                if (!File.Exists(logFile))
                {
                    File.WriteAllText(logFile, string.Empty, utf8Bom);
                }
                File.AppendAllText(logFile, $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] [{entry.Source}] {entry.Message}{Environment.NewLine}", utf8Bom);
            }
            catch
            {
                // Avoid crashing application if disk logging fails
            }
        }

        EntryAdded?.Invoke(entry);
    }

    public void ClearInMemory()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }
}
