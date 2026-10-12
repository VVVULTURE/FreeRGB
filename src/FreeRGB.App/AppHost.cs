using System.Diagnostics;
using FreeRGB.Core;
using FreeRGB.Core.Backends;

namespace FreeRGB.App;

/// <summary>Owns the long-lived services (settings, profiles, backend, lighting) independent of any window.</summary>
public sealed class AppHost : IAsyncDisposable
{
    public static AppHost Instance { get; set; } = null!;

    // Other RGB software that may be managing the same hardware (process names, case-insensitive, no extension).
    // This list is a heuristic for the diagnostics page, not a statement about what those programs do.
    private static readonly string[] KnownRgbApps =
    {
        "SignalRgb", "iCUE", "LGHUB", "lghub_agent", "OpenRGB", "AuraService", "LightingService", "ArmouryCrate.Service",
        "MysticLight", "RGBFusion", "Razer Synapse 3", "RazerCentralService", "NZXT CAM", "CAM", "Corsair.Service",
    };

    private readonly CancellationTokenSource _cts = new();
    private Task? _reconnectTask;
    private Process? _launchedOpenRgb;

    public AppHost(string dataDir, string[] args, SingleInstance instance)
    {
        DataDir = dataDir;
        Args = args;
        Single = instance;
        Settings = AppSettings.Load(dataDir);
        Profiles = new ProfileStore(Path.Combine(dataDir, "profiles"));
        ExePath = Environment.ProcessPath ?? "";
        StartHidden = Settings.StartMinimized && args.Contains(StartupManager.MinimizedArg);

        Profile profile;
        try { profile = Profiles.Exists(Settings.ActiveProfile) ? Profiles.Load(Settings.ActiveProfile) : new Profile { Name = Settings.ActiveProfile }; }
        catch (Exception ex) when (ex is ProfileValidationException or IOException)
        {
            StartupWarnings.Add($"Could not load profile '{Settings.ActiveProfile}': {ex.Message} Starting with an empty profile.");
            profile = new Profile { Name = Settings.ActiveProfile };
        }
        Backend = new OpenRgbBackend(Settings.OpenRgbHost, Settings.OpenRgbPort);
        Lighting = new LightingService(Backend, profile, Settings.EffectFps);
    }

    public string DataDir { get; }
    public string[] Args { get; }
    public SingleInstance Single { get; }
    public AppSettings Settings { get; }
    public ProfileStore Profiles { get; }
    public OpenRgbBackend Backend { get; }
    public LightingService Lighting { get; }
    public string ExePath { get; }
    public bool StartHidden { get; }
    public List<string> StartupWarnings { get; } = new();

    public event Action? ConnectionAttempted;

    public void SaveSettings()
    {
        try { Settings.Save(DataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StartupWarnings.Add("Could not save settings: " + ex.Message); }
    }

    public void SaveActiveProfile()
    {
        try
        {
            Profiles.Save(Lighting.Profile);
            Settings.ActiveProfile = Lighting.Profile.Name;
            SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProfileValidationException)
        {
            StartupWarnings.Add("Could not save profile: " + ex.Message);
        }
    }

    /// <summary>Starts the connect/retry loop. Retries use exponential backoff (2 s → 60 s) and only run while disconnected.</summary>
    public void StartBackend()
    {
        _reconnectTask = Task.Run(async () =>
        {
            var delay = TimeSpan.FromSeconds(2);
            while (!_cts.IsCancellationRequested)
            {
                if (Backend.Status.State != BackendState.Connected)
                {
                    await TryConnectOnceAsync();
                    delay = Backend.Status.State == BackendState.Connected ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
                }
                try
                {
                    // While connected we wake rarely just to notice a lost connection; the read loop already reports it.
                    await Task.Delay(Backend.Status.State == BackendState.Connected ? TimeSpan.FromSeconds(5) : delay, _cts.Token);
                }
                catch (OperationCanceledException) { return; }
            }
        });
    }

    public async Task TryConnectOnceAsync()
    {
        if (Settings.AutoLaunchOpenRgb && !string.IsNullOrWhiteSpace(Settings.OpenRgbExePath) &&
            !await OpenRgbLauncher.IsPortOpenAsync(Settings.OpenRgbHost, Settings.OpenRgbPort, TimeSpan.FromMilliseconds(400)) &&
            (_launchedOpenRgb is null || _launchedOpenRgb.HasExited))
        {
            try
            {
                _launchedOpenRgb = OpenRgbLauncher.Launch(Settings.OpenRgbExePath!, Settings.OpenRgbPort);
                await Task.Delay(2500, _cts.Token); // OpenRGB needs a moment to scan hardware before serving
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { StartupWarnings.Add("Could not start OpenRGB: " + ex.Message); }
        }
        await Backend.ConnectAsync(_cts.Token);
        ConnectionAttempted?.Invoke();
        if (Backend.Status.State == BackendState.Connected) await Lighting.ApplyAllAsync();
    }

    public async Task RescanAsync()
    {
        if (Backend.Status.State == BackendState.Connected) await Backend.RescanAsync(_cts.Token);
        else await TryConnectOnceAsync();
    }

    /// <summary>Names of known RGB programs currently running. Called on demand only (rescan / diagnostics), never polled.</summary>
    public static IReadOnlyList<string> DetectOtherRgbApps()
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (KnownRgbApps.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) found.Add(p.ProcessName);
            }
            catch { }
            finally { p.Dispose(); }
        }
        return found.ToList();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_reconnectTask is not null) { try { await _reconnectTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
        await Lighting.DisposeAsync();
        await Backend.DisposeAsync();
        // Only terminate an OpenRGB we started ourselves.
        try { if (_launchedOpenRgb is { HasExited: false }) _launchedOpenRgb.CloseMainWindow(); } catch { }
        _launchedOpenRgb?.Dispose();
        Single.Dispose();
        _cts.Dispose();
    }
}
