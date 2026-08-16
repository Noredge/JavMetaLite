using System.Text;

namespace JavMetaLite.Core.Services;

public static class AppLog
{
    private static readonly object SyncRoot = new();
    private static string? _configuredDirectory;
    private static bool _pruned;

    public static string LogDirectory => _configuredDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JavMetaLite",
        "Logs");

    public static string CurrentLogPath => Path.Combine(
        LogDirectory,
        $"JavMetaLite-{DateTime.Now:yyyyMMdd}.log");

    public static void ConfigureDirectory(string? directory)
    {
        lock (SyncRoot)
        {
            _configuredDirectory = string.IsNullOrWhiteSpace(directory)
                ? null
                : Path.GetFullPath(directory);
            _pruned = false;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warning(string message, Exception? exception = null) =>
        Write("WARN", message, exception);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                PruneOldLogs();

                var builder = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                    .Append(" [")
                    .Append(level)
                    .Append("] ")
                    .AppendLine(message.ReplaceLineEndings(" "));
                if (exception is not null)
                {
                    builder.AppendLine(exception.ToString());
                }

                File.AppendAllText(CurrentLogPath, builder.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never interrupt metadata editing or file recovery.
        }
    }

    private static void PruneOldLogs()
    {
        if (_pruned)
        {
            return;
        }

        _pruned = true;
        var cutoff = DateTime.UtcNow.AddDays(-14);
        foreach (var path in Directory.EnumerateFiles(LogDirectory, "JavMetaLite-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A locked or protected old log can safely remain in place.
            }
        }
    }
}
