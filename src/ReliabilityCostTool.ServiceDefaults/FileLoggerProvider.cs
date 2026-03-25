using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace ReliabilityCostTool.ServiceDefaults;

/// <summary>
/// A lightweight file logging provider that writes structured log entries
/// to a daily rolling log file under the application's "logs" directory.
/// Integrated into the Aspire ServiceDefaults so every project that calls
/// AddServiceDefaults() automatically gets file logging.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _writeLock = new();
    private StreamWriter? _writer;
    private string _currentDate = string.Empty;

    public FileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    public void Dispose()
    {
        lock (_writeLock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal void WriteEntry(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        var now = DateTimeOffset.Now;
        var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        lock (_writeLock)
        {
            if (_currentDate != today)
            {
                _writer?.Dispose();
                var filePath = Path.Combine(_logDirectory, $"log-{today}.log");
                _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
                _currentDate = today;
            }

            var timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
            var level = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "NON"
            };

            _writer!.WriteLine($"[{timestamp}] [{level}] [{categoryName}] {message}");
            if (exception is not null)
            {
                _writer.WriteLine($"  Exception: {exception}");
            }
        }
    }

    private sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.WriteEntry(categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}
