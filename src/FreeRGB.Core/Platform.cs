using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;

namespace FreeRGB.Core;

/// <summary>Start-with-OS support: HKCU Run key on Windows, XDG autostart .desktop file on Linux.</summary>
public static class StartupManager
{
    public const string AppKey = "FreeRGB";
    public const string MinimizedArg = "--minimized";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static string LinuxAutostartPath(string? configHome = null)
    {
        configHome ??= Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "autostart", "freergb.desktop");
    }

    public static string RunCommand(string exePath) => $"\"{exePath}\" {MinimizedArg}";

    public static string DesktopEntry(string exePath) =>
        "[Desktop Entry]\nType=Application\nName=FreeRGB\nComment=Lightweight RGB control\n" +
        $"Exec=\"{exePath.Replace("\"", "\\\"")}\" {MinimizedArg}\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";

    public static bool IsEnabled(string exePath, string appKey = AppKey)
    {
        if (OperatingSystem.IsWindows())
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(appKey) is string v && v.Contains(exePath, StringComparison.OrdinalIgnoreCase);
        }
        if (OperatingSystem.IsLinux()) return File.Exists(LinuxAutostartPath());
        return false;
    }

    public static void SetEnabled(bool enable, string exePath, string appKey = AppKey)
    {
        if (OperatingSystem.IsWindows())
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enable) k.SetValue(appKey, RunCommand(exePath));
            else k.DeleteValue(appKey, false);
        }
        else if (OperatingSystem.IsLinux())
        {
            var path = LinuxAutostartPath();
            if (enable) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, DesktopEntry(exePath)); }
            else if (File.Exists(path)) File.Delete(path);
        }
        else throw new PlatformNotSupportedException("Autostart is only implemented for Windows and Linux.");
    }
}

/// <summary>
/// Ensures one running instance per user. A second launch asks the first to show its window and exits.
/// Uses an exclusive lock file (portable) plus a user-only named pipe for the "show" message.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _pipeName;
    private FileStream? _lock;
    private CancellationTokenSource? _cts;

    public event Action? ShowRequested;

    public SingleInstance(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        LockPath = Path.Combine(dataDir, "instance.lock");
        // Keyed by the data directory so instances with different data dirs (portable use, tests) never collide.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).ToLowerInvariant())))[..16];
        _pipeName = "FreeRGB-" + hash;
    }

    public string LockPath { get; }

    /// <summary>True if this process is now the primary instance.</summary>
    public bool TryAcquire()
    {
        try
        {
            _lock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException) { return false; }
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenAsync(_cts.Token));
        return true;
    }

    /// <summary>Tells the primary instance to show itself.</summary>
    public async Task<bool> SignalExistingAsync()
    {
        try
        {
            using var c = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            await c.ConnectAsync(1500);
            c.WriteByte(1);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { return false; }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct);
                if (server.ReadByte() == 1) ShowRequested?.Invoke();
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { await Task.Delay(500, CancellationToken.None); }
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _lock?.Dispose();
        _cts?.Dispose();
    }
}

/// <summary>Optionally launches OpenRGB in headless server mode when its port is closed and the user opted in.</summary>
public static class OpenRgbLauncher
{
    // Flags per OpenRGB's CLI. NOTE: to be re-verified against `OpenRGB --help` of the installed version (see docs/WORKLOG.md).
    public static readonly string[] ServerArgs = { "--server", "--noautoconnect", "--startminimized" };

    public static async Task<bool> IsPortOpenAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var c = new TcpClient();
            using var cts = new CancellationTokenSource(timeout);
            await c.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch { return false; }
    }

    public static Process? Launch(string exePath, int port)
    {
        if (!File.Exists(exePath)) return null;
        var psi = new ProcessStartInfo(exePath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exePath)! };
        foreach (var a in ServerArgs) psi.ArgumentList.Add(a);
        if (port != 6742) { psi.ArgumentList.Add("--server-port"); psi.ArgumentList.Add(port.ToString()); }
        return Process.Start(psi);
    }
}
