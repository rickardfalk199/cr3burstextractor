using Microsoft.Extensions.Logging;

namespace Cr3BurstExtractor;

/// <summary>
/// Minimal append-only logger that writes one timestamped line per log call
/// to <see cref="SharedPaths.ServiceLogFile"/>. Rolls the file at 5MB to a
/// <c>.1</c> sibling so it doesn't grow without bound on a long-running
/// service. Intended for end-user troubleshooting — Event Viewer is the
/// official channel, but the text file is easier to point a user at.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    const long MaxBytes = 5 * 1024 * 1024;
    readonly object _gate = new();
    readonly string _path;

    public FileLoggerProvider(string path) { _path = path; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    internal void Append(string line)
    {
        lock (_gate)
        {
            try
            {
                SharedPaths.EnsureDir();
                if (File.Exists(_path) && new FileInfo(_path).Length >= MaxBytes)
                {
                    string rolled = _path + ".1";
                    if (File.Exists(rolled)) File.Delete(rolled);
                    File.Move(_path, rolled);
                }
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch
            {
                /* best-effort — never throw out of a logger */
            }
        }
    }

    sealed class FileLogger : ILogger
    {
        readonly FileLoggerProvider _provider;
        readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                                Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string msg = formatter(state, exception);
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_category}: {msg}";
            if (exception != null) line += $"{Environment.NewLine}{exception}";
            _provider.Append(line);
        }
    }
}
