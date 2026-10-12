using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using FreeRGB.Core;
using FreeRGB.Core.Backends;

namespace FreeRGB.App;

public partial class App : Application
{
    private TrayIcon? _tray;
    private NativeMenu? _profileMenu;
    private MainWindow? _window;
    private bool _exiting;
    private bool _allOff;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var host = AppHost.Instance;
            _window = new MainWindow(host);
            _window.Closing += OnWindowClosing;
            SetupTray(host);
            host.Single.ShowRequested += () => { Log.Info("Show requested by second launch."); Dispatcher.UIThread.Post(ShowWindow); };
            desktop.Exit += (_, _) => { _tray?.Dispose(); };

            // Started at login with --minimized: never create a visible window, live in the tray.
            if (!host.StartHidden) { desktop.MainWindow = _window; }

            host.Lighting.DevicesChanged += () => Dispatcher.UIThread.Post(RebuildTrayProfiles);
            host.StartBackend();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting) return;
        if (AppHost.Instance.Settings.CloseToTray && _tray is not null)
        {
            e.Cancel = true;          // closing hides to tray; "Exit" in the tray menu really quits
            _window!.Hide();
            _window.ShowInTaskbar = false;
        }
        else _ = ExitAsync();
    }

    private void ShowWindow()
    {
        Log.Info("ShowWindow()");
        if (_window is null) return;
        _window.ShowInTaskbar = true;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _tray?.Dispose();
        await AppHost.Instance.DisposeAsync();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // ---- tray ------------------------------------------------------------------------------------
    private void SetupTray(AppHost host)
    {
        using var s = AssetLoader.Open(new Uri("avares://FreeRGB/Assets/icon.png"));
        var menu = new NativeMenu();

        var open = new NativeMenuItem("Open FreeRGB");
        open.Click += (_, _) => ShowWindow();

        var toggle = new NativeMenuItem("Turn all lights off");
        toggle.Click += async (_, _) =>
        {
            _allOff = !_allOff;
            toggle.Header = _allOff ? "Turn all lights on" : "Turn all lights off";
            foreach (var d in host.Backend.Devices.ToArray())
            {
                host.Lighting.GetOrCreateSetting(d).On = !_allOff;
                await host.Lighting.ApplyDeviceAsync(d);
            }
            host.SaveActiveProfile();
        };

        _profileMenu = new NativeMenu();
        var profiles = new NativeMenuItem("Profiles") { Menu = _profileMenu };

        var rescan = new NativeMenuItem("Rescan devices");
        rescan.Click += async (_, _) => await host.RescanAsync();

        var settings = new NativeMenuItem("Settings…");
        settings.Click += (_, _) => { ShowWindow(); _window?.ShowTab("Settings"); };

        var exit = new NativeMenuItem("Exit FreeRGB");
        exit.Click += async (_, _) => await ExitAsync();

        menu.Items.Add(open); menu.Items.Add(toggle); menu.Items.Add(profiles);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(rescan); menu.Items.Add(settings);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);

        _tray = new TrayIcon { Icon = new WindowIcon(s), ToolTipText = "FreeRGB", Menu = menu, IsVisible = true };
        _tray.Clicked += (_, _) => ShowWindow();
        RebuildTrayProfiles();
    }

    public void RebuildTrayProfiles()
    {
        if (_profileMenu is null) return;
        var host = AppHost.Instance;
        _profileMenu.Items.Clear();
        foreach (var name in host.Profiles.List())
        {
            var item = new NativeMenuItem(name) { ToggleType = MenuItemToggleType.Radio, IsChecked = name == host.Lighting.Profile.Name };
            var n = name;
            item.Click += async (_, _) =>
            {
                try { await host.Lighting.LoadProfileAsync(host.Profiles.Load(n)); host.Settings.ActiveProfile = n; host.SaveSettings(); RebuildTrayProfiles(); }
                catch (Exception ex) when (ex is ProfileValidationException or IOException) { _window?.ShowStatus("Could not load profile: " + ex.Message); }
            };
            _profileMenu.Items.Add(item);
        }
        if (_profileMenu.Items.Count == 0) _profileMenu.Items.Add(new NativeMenuItem("(no saved profiles)") { IsEnabled = false });
    }
}
