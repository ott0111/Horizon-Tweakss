using System.Reflection;
using System.Text;
using Horizon.Core.Interfaces;

namespace Horizon.Infrastructure.Persistence;

public sealed class FileAppLogger : IAppLogger
{
    private const long MaximumLogBytes = 4 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _version;
    private string? _lastErrorFingerprint;
    private DateTimeOffset _lastErrorAt;

    public FileAppLogger() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Horizon", "Logs")) { }

    public FileAppLogger(string logDirectory)
    {
        LogDirectory = logDirectory;
        _version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        TryCreateDirectory();
    }

    public string LogDirectory { get; }

    public void Info(string operation, string message, string? page = null) =>
        Write("INFO", operation, page, message, null);

    public void Error(string operation, Exception exception, string? page = null, string? message = null) =>
        Write("ERROR", operation, page, message ?? exception.Message, exception);

    private void Write(string level, string operation, string? page, string message, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                if (exception is not null)
                {
                    var now = DateTimeOffset.UtcNow;
                    var fingerprint = $"{level}|{operation}|{exception.GetType().FullName}|{exception.Message}";
                    if (string.Equals(fingerprint, _lastErrorFingerprint, StringComparison.Ordinal) && now - _lastErrorAt < TimeSpan.FromMinutes(1))
                        return;
                    _lastErrorFingerprint = fingerprint;
                    _lastErrorAt = now;
                }
                TryCreateDirectory();
                var path = Path.Combine(LogDirectory, $"horizon-{DateTime.UtcNow:yyyyMMdd}.log");
                RotateIfNeeded(path);
                var entry = new StringBuilder()
                    .Append('[').Append(DateTimeOffset.UtcNow.ToString("O")).Append("] ")
                    .Append(level).Append(" version=").Append(_version)
                    .Append(" operation=").Append(Sanitize(operation))
                    .Append(" page=").Append(Sanitize(page ?? "unknown"))
                    .AppendLine()
                    .AppendLine(message);
                if (exception is not null) AppendException(entry, exception, 0);
                entry.AppendLine("---");
                File.AppendAllText(path, entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging is a diagnostic boundary and must never become a second application failure.
        }
    }

    private static void AppendException(StringBuilder builder, Exception exception, int depth)
    {
        builder.Append("exception[").Append(depth).Append("]=")
            .Append(exception.GetType().FullName).Append(": ").AppendLine(exception.Message);
        builder.Append("stack=").AppendLine(string.IsNullOrWhiteSpace(exception.StackTrace) ? "(unavailable)" : exception.StackTrace);
        if (exception.InnerException is not null) AppendException(builder, exception.InnerException, depth + 1);
    }

    private void TryCreateDirectory()
    {
        try { Directory.CreateDirectory(LogDirectory); }
        catch { }
    }
    private static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < MaximumLogBytes) return;
            var archive = Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.UtcNow:HHmmssfff}.log");
            File.Move(path, archive);
        }
        catch { }
    }
    private static string Sanitize(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
