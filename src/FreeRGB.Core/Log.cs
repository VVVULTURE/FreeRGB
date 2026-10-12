namespace FreeRGB.Core;

/// <summary>Tiny append-only diagnostic log in the data directory (truncated when it passes 256 KiB). Never throws.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            _path = Path.Combine(dataDir, "freergb.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > 256 * 1024) File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _path = null; }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var p = _path;
        if (p is null) return;
        try { lock (Gate) File.AppendAllText(p, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
