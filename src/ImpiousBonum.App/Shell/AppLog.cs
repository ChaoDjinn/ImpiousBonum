using System.Diagnostics;
using System.IO;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// The dashboard's log, dashboard.log in the settings folder: startup, updates, the sensor service, provider warnings
/// (anything written with <see cref="Trace"/>) and unhandled errors. Nothing is written until <see cref="Start"/>.
/// </summary>
public static class AppLog
{
    private static LogFile? _file;

    public static void Start(string? version)
    {
        _file = new LogFile(AppPaths.LogFile);
        Trace.Listeners.Add(new Listener());
        Info($"Impious Bonum {version ?? "(development build)"} started on {Environment.OSVersion.VersionString}");
    }

    public static void Info(string message) => _file?.Write("INFO", message);

    public static void Warning(string message) => _file?.Write("WARN", message);

    public static void Error(string message, Exception exception) => _file?.Write("ERROR", $"{message}: {exception}");

    /// <summary>Sends <c>Trace.TraceWarning</c> and friends, as used by the core library, to the log.</summary>
    private sealed class Listener : TraceListener
    {
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message) =>
            Log(eventType, message ?? "");

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args) =>
            Log(eventType, args is { Length: > 0 } ? string.Format(format ?? "", args) : format ?? "");

        public override void Write(string? message) => Info(message ?? "");

        public override void WriteLine(string? message) => Info(message ?? "");

        private static void Log(TraceEventType eventType, string message)
        {
            if (eventType <= TraceEventType.Error)
                _file?.Write("ERROR", message);
            else if (eventType == TraceEventType.Warning)
                Warning(message);
            else
                Info(message);
        }
    }
}

/// <summary>
/// A small text log. Over 1 MB at startup it moves to <c>.old</c> and starts again. A warning or error that repeats
/// (a provider failing every second, the same error on every tick) is written only the first time in each run.
/// Writing never throws.
/// </summary>
internal sealed class LogFile
{
    private const long MaxFileSize = 1024 * 1024;
    private const int MaxRemembered = 1000;
    private readonly Lock _gate = new();
    private readonly HashSet<string> _written = [];
    private readonly string _path;

    public LogFile(string path)
    {
        _path = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > MaxFileSize)
                File.Move(path, path + ".old", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";
        lock (_gate)
        {
            if (level != "INFO" && (_written.Count < MaxRemembered ? !_written.Add(message) : _written.Contains(message)))
                return;
            try
            {
                File.AppendAllText(_path, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the dashboard down.
            }
        }
    }
}
