namespace ImpiousBonum.Sensors;

/// <summary>Console when run interactively; otherwise a small rolling file in %ProgramData%\ImpiousBonum.</summary>
internal static class Log
{
    private const long MaxFileSize = 1024 * 1024;
    private static readonly Lock Gate = new();
    private static string? _file;

    public static void UseFile()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ImpiousBonum");
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "sensors.log");
        if (File.Exists(_file) && new FileInfo(_file).Length > MaxFileSize)
            File.Move(_file, _file + ".old", overwrite: true);
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        lock (Gate)
        {
            if (_file is null)
                Console.WriteLine(line);
            else
                File.AppendAllText(_file, line + Environment.NewLine);
        }
    }
}
