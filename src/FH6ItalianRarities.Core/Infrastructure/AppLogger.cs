using System.Globalization;
using System.IO;
using System.Text;

namespace FH6ItalianRarities.Infrastructure;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FH6ItalianRarities");

    public static string LogDirectory { get; } = Path.Combine(Root, "Logs");

    public static string CurrentLogPath { get; private set; } = Path.Combine(LogDirectory, "latest.log");

    public static void Initialize()
    {
        Directory.CreateDirectory(LogDirectory);
        CurrentLogPath = Path.Combine(LogDirectory, $"FH6ItalianRarities-{DateTime.Now:yyyyMMdd}.log");
        try
        {
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Log retention must never prevent startup.
        }

        Info($"Application started. Version={typeof(AppLogger).Assembly.GetName().Version}; " +
             $"OS={Environment.OSVersion}; x64={Environment.Is64BitProcess}");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                var line = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss.fff zzz",
                        CultureInfo.InvariantCulture))
                    .Append(" [").Append(level).Append("] ")
                    .AppendLine(message)
                    .ToString();
                File.AppendAllText(CurrentLogPath, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics should never make a game operation fail.
        }
    }
}
