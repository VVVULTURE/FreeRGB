using Avalonia.Input.Platform;
using System.Net.Http;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FreeRGB.Core;
using FreeRGB.Core.Backends;

namespace FreeRGB.App;

public sealed record DeviceRow(string Id, string Name, string Subtitle, IBrush Dot, DeviceInfo? Device);

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private DeviceInfo? _selected;
    private string? _selectedAbsentId;
    private bool _loading;
    private CancellationTokenSource? _applyDebounce;
    private CancellationTokenSource? _saveDebounce;

    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#3FB950"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#D29922"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F85149"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#8B949E"));

    // Needed by the XAML loader / designer.
    public MainWindow() : this(AppHost.Instance) { }

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        DeviceList.ItemTemplate = new FuncDataTemplate<DeviceRow>((row, _) =>
        {
            var name = new TextBlock { Text = row.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var sub = new TextBlock { Text = row.Subtitle, FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
            return new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, Fill = row.Dot, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center },
                    new StackPanel { [Grid.ColumnProperty] = 1, Children = { name, sub } },
                },
            };
        });

        DeviceList.SelectionChanged += (_, _) => OnDeviceSelected();
        RescanBtn.Click += async (_, _) => { ShowStatus("Rescanning…"); await _host.RescanAsync(); RefreshDiagnostics(); };

        ProfileBox.SelectionChanged += async (_, _) => await OnProfileChosen();
        SaveProfileBtn.Click += (_, _) => { _host.SaveActiveProfile(); ShowStatus($"Saved profile '{_host.Lighting.Profile.Name}'."); };
        NewProfileBtn.Click += async (_, _) => await SaveProfileAsAsync();
        DeleteProfileBtn.Click += (_, _) => DeleteProfile();
        ImportBtn.Click += async (_, _) => await ImportProfileAsync();
        ExportBtn.Click += async (_, _) => await ExportProfileAsync();

        PowerSwitch.IsCheckedChanged += (_, _) => OnControlChanged();
        EffectBox.SelectionChanged += (_, _) => { if (!_loading) { UpdateControlVisibility(); OnControlChanged(); } };
        Picker.ColorChanged += (_, e) => { if (!_loading) { _loading = true; HexBox.Text = ToHex(e.NewColor); _loading = false; OnControlChanged(); } };
        HexBox.TextChanged += (_, _) =>
        {
            if (_loading || !Rgb.TryParseHex(HexBox.Text, out var c)) return;
            _loading = true; Picker.Color = Color.FromRgb(c.R, c.G, c.B); _loading = false; OnControlChanged();
        };
        BrightnessSlider.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) OnControlChanged(); };
        SpeedSlider.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty) OnControlChanged(); };

        AllApplyBtn.Click += async (_, _) => await ApplyAllAsync(on: true);
        AllOffBtn.Click += async (_, _) => await ApplyAllAsync(on: false);

        DiagRefreshBtn.Click += (_, _) => RefreshDiagnostics();
        DiagCopyBtn.Click += async (_, _) => { if (Clipboard is { } cb) await cb.SetTextAsync(DiagText.Text ?? ""); };
        Tabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.Source, Tabs) && Tabs.SelectedItem == DiagTab) RefreshDiagnostics(); };

        SaveSettingsBtn.Click += (_, _) => SaveSettings();
        SetupBtn.Click += async (_, _) => await RunHardwareSetupAsync();
        BrowseOpenRgbBtn.Click += async (_, _) => await BrowseOpenRgb();

        _host.Lighting.DevicesChanged += () => Dispatcher.UIThread.Post(RefreshDevices);
        _host.Lighting.Applied += r => Dispatcher.UIThread.Post(() => { if (!r.Ok) ShowStatus(r.Message, warn: true); });

        LoadSettingsUi();
        UpdateSetupStatus();
        RefreshProfiles();
        RefreshDevices();
        foreach (var w in _host.StartupWarnings) ShowStatus(w, warn: true);
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public void ShowTab(string header)
    {
        Tabs.SelectedItem = header switch { "Settings" => SettingsTab, "Diagnostics" => DiagTab, _ => LightsTab };
    }

    public void ShowStatus(string text, bool warn = false)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ShowStatus(text, warn)); return; }
        StatusText.Text = text;
    }

    // ---- devices ---------------------------------------------------------------------------------
    private void RefreshDevices()
    {
        var backend = _host.Backend;
        var rows = new List<DeviceRow>();
        foreach (var d in backend.Devices)
        {
            var ok = d.Controllability == Controllability.Controllable;
            rows.Add(new DeviceRow(d.Id, d.Name, $"{TypeText(d.Type)} · {(ok ? $"{d.LedCount} LEDs" : "detected, not controllable")}", ok ? Green : Amber, d));
        }
        foreach (var kv in _host.Lighting.AbsentSettings())
            rows.Add(new DeviceRow(kv.Key, kv.Value.DeviceName ?? kv.Key, "not connected — settings kept", Grey, null));

        var prevId = _selected?.Id ?? _selectedAbsentId;
        _loading = true;
        DeviceList.ItemsSource = rows;
        _loading = false;
        var again = rows.FirstOrDefault(r => r.Id == prevId) ?? rows.FirstOrDefault(r => r.Device is not null);
        if (again is not null) DeviceList.SelectedItem = again; else OnDeviceSelected();

        var st = backend.Status;
        BackendDotFill(st.State switch { BackendState.Connected => Green, BackendState.Connecting => Amber, _ => Red });
        StatusText.Text = st.Message;
        RefreshProfiles();
        if (Tabs.SelectedItem == DiagTab) RefreshDiagnostics();
    }

    private void BackendDotFill(IBrush b) => BackendDot.Fill = b;

    private static string TypeText(DeviceType t) => t switch
    {
        DeviceType.Dram => "RAM", DeviceType.Gpu => "Graphics card", DeviceType.Cooler => "Cooler / fans", DeviceType.LedStrip => "LED strip",
        DeviceType.HeadsetStand => "Headset stand", DeviceType.Mousemat => "Mousepad", _ => t.ToString(),
    };

    private void OnDeviceSelected()
    {
        if (_loading) return;
        var row = DeviceList.SelectedItem as DeviceRow;
        _selected = row?.Device;
        _selectedAbsentId = row is { Device: null } ? row.Id : null;
        LoadControls();
    }

    private void LoadControls()
    {
        _loading = true;
        try
        {
            var d = _selected;
            var devs = _host.Backend.Devices;
            EmptyHint.IsVisible = d is null && _selectedAbsentId is null;
            DeviceHeader.IsVisible = d is not null || _selectedAbsentId is not null;
            Controls.IsVisible = false;
            DevWarning.IsVisible = false;

            if (d is null && _selectedAbsentId is null)
            {
                EmptyHint.Text = _host.Backend.Status.State == BackendState.Connected
                    ? "OpenRGB is running but reports no devices. Check the Diagnostics tab."
                    : "No devices yet. FreeRGB needs the OpenRGB SDK server running — see the Diagnostics tab for what to do.";
                return;
            }
            if (d is null)
            {
                var s = _host.Lighting.Profile.Devices[_selectedAbsentId!];
                DevTitle.Text = s.DeviceName ?? _selectedAbsentId;
                DevSub.Text = "Not connected";
                DevWarningText.Text = "This device is saved in the active profile but is not currently detected. Its settings are kept and will be applied automatically when it reappears.";
                DevWarning.IsVisible = true;
                return;
            }

            DevTitle.Text = d.Name;
            DevSub.Text = string.Join(" · ", new[] { d.Vendor, TypeText(d.Type), d.Location }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (d.Controllability != Controllability.Controllable)
            {
                DevWarningText.Text = d.WhyNotControllable;
                DevWarning.IsVisible = true;
                return;
            }

            Controls.IsVisible = true;
            var set = _host.Lighting.GetOrCreateSetting(d);
            var effects = LightingService.AvailableEffects(d);
            EffectBox.ItemsSource = effects;
            if (!effects.Contains(set.Effect)) set.Effect = effects.FirstOrDefault() ?? EffectNames.Static;
            EffectBox.SelectedItem = set.Effect;
            PowerSwitch.IsChecked = set.On;
            Rgb.TryParseHex(set.Color, out var c);
            Picker.Color = Color.FromRgb(c.R, c.G, c.B);
            HexBox.Text = set.Color;
            BrightnessSlider.Value = set.Brightness;
            SpeedSlider.Value = set.Speed;
            BuildZones(d, set);
            UpdateControlVisibility();
        }
        finally { _loading = false; }
    }

    private void UpdateControlVisibility()
    {
        if (_selected is null || EffectBox.SelectedItem is not string fx) return;
        var (speed, bright, color) = LightingService.ControlsFor(_selected, fx);
        SpeedPanel.IsVisible = speed;
        BrightnessPanel.IsVisible = bright;
        ColorPanel.IsVisible = color;
        ZoneExpander.IsVisible = ZoneList.Children.Count > 0;
    }

    private void BuildZones(DeviceInfo d, DeviceSetting set)
    {
        ZoneList.Children.Clear();
        bool colourZones = d.Zones.Count >= 2 && d.DirectMode is not null;
        if (!colourZones && !d.Zones.Any(z => z.Resizable)) return;
        for (int zi = 0; zi < d.Zones.Count; zi++)
        {
            var z = d.Zones[zi];
            var zoneIndex = zi;
            var zoneName = z.Name;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = z.Resizable ? z.Name : $"{z.Name} ({z.LedsCount} LEDs)", Width = 220, VerticalAlignment = VerticalAlignment.Center });
            if (z.Resizable)
            {
                // Addressable headers: the hardware cannot report how many LEDs are connected, so the user states it.
                var count = new NumericUpDown { Minimum = z.LedsMin, Maximum = z.LedsMax, FormatString = "0", Width = 120,
                    Value = set.ZoneSizes.TryGetValue(zoneName, out var saved) ? saved : z.LedsCount };
                count.ValueChanged += (_, e) =>
                {
                    if (_loading || _selected is null || e.NewValue is null) return;
                    _host.Lighting.GetOrCreateSetting(_selected).ZoneSizes[zoneName] = (int)e.NewValue.Value;
                    ScheduleApply();
                };
                row.Children.Add(count);
                row.Children.Add(new TextBlock { Text = "LEDs connected", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
                var find = new Button { Content = "Find automatically\u2026" };
                find.Click += async (_, _) => await FindLedCountAsync(d, zoneIndex, count);
                row.Children.Add(find);
            }
            if (colourZones)
            {
                var box = new TextBox { Width = 110, MaxLength = 7, PlaceholderText = "(main colour)", Text = set.Zones.GetValueOrDefault(zoneName) };
                box.TextChanged += (_, _) =>
                {
                    if (_loading || _selected is null) return;
                    var s = _host.Lighting.GetOrCreateSetting(_selected);
                    if (string.IsNullOrWhiteSpace(box.Text)) s.Zones.Remove(zoneName);
                    else if (Rgb.TryParseHex(box.Text, out _)) s.Zones[zoneName] = box.Text!.StartsWith('#') ? box.Text : "#" + box.Text;
                    else return;
                    ScheduleApply();
                };
                row.Children.Add(box);
            }
            ZoneList.Children.Add(row);
        }
    }

    // ---- applying changes ------------------------------------------------------------------------
    private void OnControlChanged()
    {
        if (_loading || _selected is null) return;
        var s = _host.Lighting.GetOrCreateSetting(_selected);
        s.On = PowerSwitch.IsChecked == true;
        if (EffectBox.SelectedItem is string fx) s.Effect = fx;
        if (Rgb.TryParseHex(HexBox.Text, out var c)) s.Color = c.ToHex();
        s.Brightness = (int)Math.Round(BrightnessSlider.Value);
        s.Speed = (int)Math.Round(SpeedSlider.Value);
        ScheduleApply();
    }

    /// <summary>Coalesces rapid edits (slider drags) into one hardware update.</summary>
    private void ScheduleApply()
    {
        var dev = _selected;
        if (dev is null) return;
        _applyDebounce?.Cancel();
        var cts = _applyDebounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(60, cts.Token);
                var r = await _host.Lighting.ApplyDeviceAsync(dev, cts.Token);
                if (!r.Ok) ShowStatus(r.Message, warn: true);
            }
            catch (OperationCanceledException) { }
        });
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _saveDebounce?.Cancel();
        var cts = _saveDebounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(1000, cts.Token); _host.SaveActiveProfile(); }
            catch (OperationCanceledException) { }
        });
    }

    private async Task ApplyAllAsync(bool on)
    {
        Rgb.TryParseHex(AllHex.Text, out var c);
        if (on && !Rgb.TryParseHex(AllHex.Text, out c)) { ShowStatus("Enter a colour like #FF8800.", warn: true); return; }
        int n = 0, failed = 0;
        foreach (var d in _host.Backend.Devices.Where(x => x.Controllability == Controllability.Controllable).ToArray())
        {
            if (!LightingService.CanStatic(d)) { failed++; continue; } // can't do a static colour: skip rather than fake it
            var s = _host.Lighting.GetOrCreateSetting(d);
            s.On = on;
            if (on) { s.Color = c.ToHex(); s.Effect = EffectNames.Static; s.Zones.Clear(); }
            var r = await _host.Lighting.ApplyDeviceAsync(d);
            if (r.Ok) n++; else failed++;
        }
        _host.SaveActiveProfile();
        LoadControls();
        ShowStatus($"Applied to {n} device(s)" + (failed > 0 ? $"; {failed} could not be set (see Diagnostics)." : "."));
    }

    // ---- profiles --------------------------------------------------------------------------------
    private void RefreshProfiles()
    {
        _loading = true;
        var names = _host.Profiles.List().ToList();
        if (!names.Contains(_host.Lighting.Profile.Name)) names.Insert(0, _host.Lighting.Profile.Name);
        ProfileBox.ItemsSource = names;
        ProfileBox.SelectedItem = _host.Lighting.Profile.Name;
        _loading = false;
    }

    private async Task OnProfileChosen()
    {
        if (_loading || ProfileBox.SelectedItem is not string name || name == _host.Lighting.Profile.Name) return;
        try
        {
            await _host.Lighting.LoadProfileAsync(_host.Profiles.Load(name));
            _host.Settings.ActiveProfile = name; _host.SaveSettings();
            ShowStatus($"Loaded profile '{name}'. Settings for devices that aren't connected are kept.");
            RefreshDevices();
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException) { ShowStatus("Could not load profile: " + ex.Message, warn: true); RefreshProfiles(); }
    }

    private async Task SaveProfileAsAsync()
    {
        var name = await PromptAsync("Save profile as", "Profile name");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var p = _host.Lighting.Profile.Clone();
            p.Name = name.Trim();
            ProfileValidator.Validate(p);
            _host.Profiles.Save(p);
            await _host.Lighting.LoadProfileAsync(p);
            _host.Settings.ActiveProfile = p.Name; _host.SaveSettings();
            RefreshProfiles();
            ShowStatus($"Saved profile '{p.Name}'.");
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException) { ShowStatus("Could not save profile: " + ex.Message, warn: true); }
    }

    private void DeleteProfile()
    {
        var name = _host.Lighting.Profile.Name;
        _host.Profiles.Delete(name);
        var next = _host.Profiles.List().FirstOrDefault();
        _ = _host.Lighting.LoadProfileAsync(next is null ? new Profile { Name = "Default" } : _host.Profiles.Load(next));
        _host.Settings.ActiveProfile = _host.Lighting.Profile.Name; _host.SaveSettings();
        RefreshProfiles(); RefreshDevices();
        ShowStatus($"Deleted profile '{name}'.");
    }

    private async Task ImportProfileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import FreeRGB profile", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("FreeRGB profile") { Patterns = new[] { "*.json" } } },
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            var p = _host.Profiles.Import(path);
            RefreshProfiles();
            ShowStatus($"Imported profile '{p.Name}'. Choose it from the Profile list to use it.");
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException or UnauthorizedAccessException) { ShowStatus("Import failed: " + ex.Message, warn: true); }
    }

    private async Task ExportProfileAsync()
    {
        _host.SaveActiveProfile();
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export profile", SuggestedFileName = ProfileStore.FileNameFor(_host.Lighting.Profile.Name),
            DefaultExtension = "json", FileTypeChoices = new[] { new FilePickerFileType("FreeRGB profile") { Patterns = new[] { "*.json" } } },
        });
        var path = f?.TryGetLocalPath();
        if (path is null) return;
        try { _host.Profiles.Export(_host.Lighting.Profile.Name, path); ShowStatus($"Exported to {path}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowStatus("Export failed: " + ex.Message, warn: true); }
    }

    private async Task<string?> PromptAsync(string title, string label)
    {
        var box = new TextBox { Width = 260, MaxLength = ProfileValidator.MaxNameLength };
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dlg = new Window
        {
            Title = title, Width = 330, Height = 150, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 10,
                Children = { new TextBlock { Text = label }, box, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } } },
            },
        };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; dlg.Close(); };
        cancel.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
        return result;
    }

    // ---- diagnostics -----------------------------------------------------------------------------
    private void RefreshDiagnostics()
    {
        var b = _host.Backend;
        var sb = new StringBuilder();
        sb.AppendLine($"FreeRGB {typeof(MainWindow).Assembly.GetName().Version}   {System.Runtime.InteropServices.RuntimeInformation.OSDescription}   .NET {Environment.Version}");
        sb.AppendLine($"Backend: {b.Name}  state={b.Status.State}  protocol={(b.Status.State == BackendState.Connected ? b.NegotiatedProtocol.ToString() : "-")}");
        sb.AppendLine($"Status : {b.Status.Message}");
        sb.AppendLine($"Data   : {_host.DataDir}");
        sb.AppendLine();

        var others = AppHost.DetectOtherRgbApps();
        if (others.Count > 0)
            sb.AppendLine($"Other RGB software running: {string.Join(", ", others)}\n  These may also control the same hardware. If colours flicker or revert, close the other program\n  (or disable its lighting control) so only one program talks to each device.\n");

        if (b.Status.State is BackendState.Unavailable or BackendState.Disconnected)
        {
            sb.AppendLine("OpenRGB is not reachable. To fix:");
            sb.AppendLine("  1. Install OpenRGB (https://openrgb.org).");
            sb.AppendLine("  2. Start it with its SDK server: OpenRGB --server   (or GUI → SDK Server tab → Start Server).");
            sb.AppendLine("  3. Or set the OpenRGB path in Settings and enable \"Start OpenRGB for me\".");
            if (OperatingSystem.IsLinux())
                sb.AppendLine("  Linux: install OpenRGB's udev rules and make sure your user can access the devices (see OpenRGB docs, Documentation/UdevRules.md).");
            sb.AppendLine();
        }

        sb.AppendLine($"Devices ({b.Devices.Count}):");
        foreach (var d in b.Devices)
        {
            sb.AppendLine($"- {d.Name}  [{d.Type}]  vendor={Nz(d.Vendor)}  serial={Nz(d.Serial)}");
            sb.AppendLine($"    location={Nz(d.Location)}  LEDs={d.LedCount}  zones={d.Zones.Count}  state={d.Controllability}");
            if (d.WhyNotControllable is { } why) sb.AppendLine($"    why: {why}");
            sb.AppendLine($"    modes: {string.Join(", ", d.Modes.Select(m => m.Name))}");
        }
        var absent = _host.Lighting.AbsentSettings();
        if (absent.Count > 0)
        {
            sb.AppendLine($"\nIn active profile but not detected ({absent.Count}):");
            foreach (var kv in absent) sb.AppendLine($"- {kv.Value.DeviceName ?? kv.Key}");
        }
        DiagText.Text = sb.ToString();
    }

    private static string Nz(string s) => string.IsNullOrWhiteSpace(s) ? "-" : s;

    // ---- settings --------------------------------------------------------------------------------
    private void LoadSettingsUi()
    {
        var s = _host.Settings;
        try { StartWithOsBox.IsChecked = StartupManager.IsSupported && StartupManager.IsEnabled(_host.ExePath); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { StartWithOsBox.IsChecked = false; }
        StartWithOsBox.IsEnabled = StartupManager.IsSupported;
        StartupNote.Text = OperatingSystem.IsWindows()
            ? "Adds a per-user entry (HKCU\\…\\Run). No administrator rights needed."
            : OperatingSystem.IsLinux() ? "Creates ~/.config/autostart/freergb.desktop (XDG autostart; honoured by most desktop environments)." : "Not available on this platform.";
        StartMinBox.IsChecked = s.StartMinimized;
        CloseToTrayBox.IsChecked = s.CloseToTray;
        PortBox.Value = s.OpenRgbPort;
        AutoLaunchBox.IsChecked = s.AutoLaunchOpenRgb;
        OpenRgbPathBox.Text = s.OpenRgbExePath;
        FpsBox.Value = s.EffectFps;
    }

    private void SaveSettings()
    {
        var s = _host.Settings;
        s.StartMinimized = StartMinBox.IsChecked == true;
        s.CloseToTray = CloseToTrayBox.IsChecked == true;
        s.OpenRgbPort = (int)(PortBox.Value ?? 6742);
        s.AutoLaunchOpenRgb = AutoLaunchBox.IsChecked == true;
        s.OpenRgbExePath = string.IsNullOrWhiteSpace(OpenRgbPathBox.Text) ? null : OpenRgbPathBox.Text.Trim();
        s.EffectFps = (int)(FpsBox.Value ?? 20);
        s.Normalize();
        _host.Lighting.SetFps(s.EffectFps);
        var notes = new List<string>();
        try
        {
            if (StartupManager.IsSupported) StartupManager.SetEnabled(StartWithOsBox.IsChecked == true, _host.ExePath);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            notes.Add("Could not change start-with-sign-in: " + ex.Message);
        }
        _host.SaveSettings();
        notes.Add("Settings saved. A changed port applies after restarting FreeRGB.");
        SettingsNote.Text = string.Join("\n", notes);
    }

    private async Task BrowseOpenRgb()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select the OpenRGB executable", AllowMultiple = false });
        var p = files.FirstOrDefault()?.TryGetLocalPath();
        if (p is not null) OpenRgbPathBox.Text = p;
    }

    // ---- hardware setup ----------------------------------------------------------------------------
    private void UpdateSetupStatus()
    {
        if (!HardwareSetup.IsSupported)
        {
            SetupStatusText.Text = "Automatic setup is Windows-only. On Linux install OpenRGB from your distribution and its udev rules (see docs/TROUBLESHOOTING.md).";
            SetupBtn.IsVisible = false; SetupExplain.IsVisible = false;
            return;
        }
        var st = _host.Setup.GetStatus();
        static string Mark(bool b) => b ? "✔" : "✘";
        SetupStatusText.Text = $"{Mark(st.OpenRgbInstalled)} OpenRGB {HardwareSetup.OpenRgbVersion} installed by FreeRGB     " +
                               $"{Mark(st.PawnIoInstalled)} PawnIO driver     {Mark(st.ElevatedTaskRegistered)} start-up task (no prompt at sign-in)";
        SetupBtn.Content = st.OpenRgbInstalled && st.PawnIoInstalled && st.ElevatedTaskRegistered ? "Re-run hardware setup" : "Set up hardware support";
    }

    private async Task RunHardwareSetupAsync()
    {
        SetupBtn.IsEnabled = false; SetupBusy.IsVisible = true;
        var lines = new List<string>();
        var progress = new Progress<string>(m => { lines.Add(m); SetupLog.Text = string.Join(Environment.NewLine, lines); });
        try
        {
            var setup = _host.Setup;
            var st = setup.GetStatus();
            if (!st.OpenRgbInstalled) await setup.InstallOpenRgbAsync(progress);
            if (!st.PawnIoInstalled || !st.ElevatedTaskRegistered)
            {
                var code = await setup.InstallElevatedPartsAsync(_host.Settings.OpenRgbPort, progress);
                if (code != 0) { UpdateSetupStatus(); return; }
            }
            _host.Settings.AutoLaunchOpenRgb = true;
            _host.Settings.OpenRgbExePath = null;
            _host.SaveSettings();
            AutoLaunchBox.IsChecked = true; OpenRgbPathBox.Text = null;
            ((IProgress<string>)progress).Report("Starting OpenRGB and connecting…");
            await _host.TryConnectOnceAsync();
            ((IProgress<string>)progress).Report(_host.Backend.Status.Message);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or TaskCanceledException)
        {
            Log.Error("Hardware setup failed", ex);
            ((IProgress<string>)progress).Report("Setup failed: " + ex.Message);
        }
        finally { SetupBusy.IsVisible = false; SetupBtn.IsEnabled = true; UpdateSetupStatus(); }
    }

    // ---- LED-count wizard ----------------------------------------------------------------------------
    /// <summary>
    /// Addressable headers cannot report their LED count, so we light single LEDs and ask. Every question is a yes/no about
    /// what the user can see; the result is only written to the profile if they confirm it.
    /// </summary>
    private async Task FindLedCountAsync(DeviceInfo device, int zoneIndex, NumericUpDown field)
    {
        var zone = device.Zones[zoneIndex];
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        var yes = new Button { Content = "Yes", MinWidth = 90 };
        var no = new Button { Content = "No", MinWidth = 90 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var bar = new ProgressBar { Minimum = 0, Maximum = 7, Value = 0, Width = 440 };
        var dlg = new Window
        {
            Title = $"Find LED count \u2014 {zone.Name}", Width = 500, Height = 290, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18), Spacing = 14,
                Children = { text, bar, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { yes, no, cancel } } },
            },
        };
        TaskCompletionSource<bool?>? pending = null;
        Task<bool?> Ask(string q, string yesText = "Yes", string noText = "No")
        {
            text.Text = q; yes.Content = yesText; no.Content = noText;
            pending = new TaskCompletionSource<bool?>();
            return pending.Task;
        }
        yes.Click += (_, _) => pending?.TrySetResult(true);
        no.Click += (_, _) => pending?.TrySetResult(false);
        cancel.Click += (_, _) => dlg.Close();
        dlg.Closed += (_, _) => pending?.TrySetResult(null);

        int? result = null;
        dlg.Opened += async (_, _) =>
        {
            try
            {
                var white = new Rgb(255, 255, 255);
                var d = await _host.Lighting.ProbeZoneAsync(device, zoneIndex, null, white);
                var max = (int)d.Zones[zoneIndex].LedsMax;
                var any = await Ask($"\"{zone.Name}\" is now sending WHITE to up to {max} LEDs.\n\nIs anything connected to this header (your fans / strip) lit white right now?");
                if (any is null) return;
                if (any == false)
                {
                    await Ask("Nothing responded. This header is probably not connected to your fans: their control cable (a small 3-pin ARGB cable from the case's RGB hub or the fans) may be plugged into the other addressable header, or not into the motherboard at all. Molex only supplies power to the lights.\n\nSet the count to 0 for now?", "Set to 0", "Leave unchanged");
                    result = 0; return;
                }
                var search = new LedCountSearch(max);
                while (!search.Done)
                {
                    bar.Value = 7 - search.QuestionsLeft;
                    d = await _host.Lighting.ProbeZoneAsync(d, zoneIndex, search.NextProbe, white);
                    var visible = await Ask("Look at all the lights on this header. Is exactly ONE white LED lit right now?\n(It moves each time. If everything is dark, answer No.)");
                    if (visible is null) return;
                    search.Answer(visible.Value);
                }
                bar.Value = 7;
                if (search.Result == 0) { await Ask("No LED was ever visible, so the count is 0.", "OK", "OK"); result = 0; return; }
                d = await _host.Lighting.ProbeZoneAsync(d, zoneIndex, search.Result - 1, white);
                var ok = await Ask($"Result: {search.Result} LEDs.\n\nTo confirm: only the LAST LED of your last fan/strip should be lit now. Is that right?", "Yes, save " + search.Result, "No");
                if (ok == true) result = search.Result;
                else if (ok == false) text.Text = "Not saved. You can also type the number yourself.";
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or ArgumentException or InvalidOperationException)
            {
                ShowStatus("LED-count wizard stopped: " + ex.Message, warn: true);
            }
            finally { dlg.Close(); }
        };
        await dlg.ShowDialog(this);

        // Put the device back to its saved state; apply the found count if the user confirmed it.
        if (result is { } n) field.Value = n;   // triggers the normal save + apply path
        else if (_selected is not null) await _host.Lighting.ApplyDeviceAsync(_selected);
        if (result is { } found) ShowStatus($"{zone.Name}: {found} LEDs saved.");
    }
}
