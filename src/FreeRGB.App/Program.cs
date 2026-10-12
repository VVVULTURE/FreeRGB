using Avalonia;
using FreeRGB.Core;

namespace FreeRGB.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var dataDir = AppSettings.DataDirectory();
        if (args.Contains("--diagnose")) return Diagnose(dataDir).GetAwaiter().GetResult();
        Log.Init(dataDir);
        using var instance = new SingleInstance(dataDir);
        if (!instance.TryAcquire())
        {
            Log.Info("Second launch: signalling the running instance.");
            // Already running: ask the first instance to show itself, then exit without starting a second UI.
            var ok = instance.SignalExistingAsync().GetAwaiter().GetResult();
            Log.Info("Signal delivered: " + ok);
            return 0;
        }

        Log.Info($"Starting. args=[{string.Join(' ', args)}]");
        AppHost.Instance = new AppHost(dataDir, args, instance);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Headless: connect to the backend, print what it reports, exit. Never writes to hardware.</summary>
    private static async Task<int> Diagnose(string dataDir)
    {
        var settings = AppSettings.Load(dataDir);
        await using var backend = new FreeRGB.Core.Backends.OpenRgbBackend(settings.OpenRgbHost, settings.OpenRgbPort);
        await backend.ConnectAsync();
        Console.WriteLine($"{backend.Name}: {backend.Status.State} - {backend.Status.Message}");
        foreach (var d in backend.Devices)
        {
            Console.WriteLine($"- {d.Name} [{d.Type}] vendor='{d.Vendor}' serial='{d.Serial}' location='{d.Location}'");
            Console.WriteLine($"    LEDs={d.LedCount} zones={d.Zones.Count} state={d.Controllability} {d.WhyNotControllable}");
            foreach (var m in d.Modes)
                Console.WriteLine($"    mode '{m.Name}' flags={m.Flags} speed={m.SpeedMin}..{m.SpeedMax} brightness={m.BrightnessMin}..{m.BrightnessMax} colors={m.ColorsMin}..{m.ColorsMax}");
        }
        return backend.Status.State == FreeRGB.Core.Backends.BackendState.Connected ? 0 : 1;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
