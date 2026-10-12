using System.Text.Json;

namespace FreeRGB.Core;

public sealed class AppSettings
{
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string ActiveProfile { get; set; } = "Default";
    public string OpenRgbHost { get; set; } = "127.0.0.1";
    public int OpenRgbPort { get; set; } = 6742;
    /// <summary>Optional path to the OpenRGB executable; if set and auto-launch is on, FreeRGB starts it in server mode when the port is closed.</summary>
    public string? OpenRgbExePath { get; set; }
    public bool AutoLaunchOpenRgb { get; set; }
    /// <summary>Frame rate cap for software-driven effects.</summary>
    public int EffectFps { get; set; } = 20;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(ActiveProfile)) ActiveProfile = "Default";
        // Only loopback hosts are accepted: FreeRGB does not talk to remote RGB servers.
        if (OpenRgbHost is not ("127.0.0.1" or "localhost" or "::1")) OpenRgbHost = "127.0.0.1";
        if (OpenRgbPort is < 1 or > 65535) OpenRgbPort = 6742;
        EffectFps = Math.Clamp(EffectFps, 5, 60);
    }

    public static string DataDirectory()
    {
        // Override for portable use and for tests/measurements: FREERGB_DATA_DIR.
        var overrideDir = Environment.GetEnvironmentVariable("FREERGB_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir)) return Path.GetFullPath(overrideDir);
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        // On Linux ApplicationData resolves to $XDG_CONFIG_HOME or ~/.config.
        return Path.Combine(baseDir, "FreeRGB");
    }

    public static AppSettings Load(string dataDir)
    {
        var path = Path.Combine(dataDir, "settings.json");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length < 64 * 1024)
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(path), FreeRgbJsonContext.Default.AppSettings) ?? new();
                s.Normalize();
                return s;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { /* fall back to defaults */ }
        return new();
    }

    public void Save(string dataDir)
    {
        Normalize();
        AtomicFile.WriteAllText(Path.Combine(dataDir, "settings.json"), JsonSerializer.Serialize(this, FreeRgbJsonContext.Default.AppSettings));
    }
}
