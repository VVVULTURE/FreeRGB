using FreeRGB.Core.Backends;

namespace FreeRGB.Core;

/// <summary>Software-driven effects, used only on devices that have a per-LED ("Direct") mode.</summary>
public static class SoftwareEffects
{
    public const string Prefix = "Software: ";
    public const string Rainbow = Prefix + "Rainbow";
    public const string ColorCycle = Prefix + "Color cycle";
    public const string Breathing = Prefix + "Breathing";
    public const string Wave = Prefix + "Wave";

    public static readonly string[] All = { Rainbow, ColorCycle, Breathing, Wave };
    public static bool IsSoftware(string effect) => effect.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Computes a frame. <paramref name="t"/> is seconds, <paramref name="speed"/> 0..100.</summary>
    public static void Render(string effect, double t, int speed, Rgb baseColor, double brightness, Span<Rgb> frame)
    {
        double rate = 0.1 + speed / 100.0 * 0.9; // cycles per second, 0.1..1.0
        int n = frame.Length;
        switch (effect)
        {
            case Rainbow:
                for (int i = 0; i < n; i++) frame[i] = Rgb.FromHsv(t * rate * 360 + (n > 1 ? i * 360.0 / n : 0), 1, brightness);
                break;
            case Wave:
                for (int i = 0; i < n; i++)
                {
                    double phase = Math.Sin(2 * Math.PI * (t * rate - (n > 1 ? i / (double)n : 0)));
                    frame[i] = baseColor.Scale(brightness * (0.5 + 0.5 * phase));
                }
                break;
            case ColorCycle:
                frame.Fill(Rgb.FromHsv(t * rate * 360, 1, brightness));
                break;
            case Breathing:
                frame.Fill(baseColor.Scale(brightness * (0.5 + 0.5 * Math.Sin(2 * Math.PI * t * rate - Math.PI / 2))));
                break;
            default:
                frame.Fill(baseColor.Scale(brightness));
                break;
        }
    }
}

public sealed record ApplyResult(string DeviceId, bool Ok, string Message);

/// <summary>
/// Applies profile settings to devices through a backend. Hardware effects are used whenever the device
/// has them; software effects run on a single shared timer that exists only while one is active.
/// </summary>
public sealed class LightingService : IAsyncDisposable
{
    private readonly IDeviceBackend _backend;
    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private readonly Dictionary<string, SoftwareRun> _running = new();
    private readonly object _runLock = new();
    private CancellationTokenSource? _engineCts;
    private Task? _engineTask;
    private int _fps = 20;
    private bool _disposed;

    public LightingService(IDeviceBackend backend, Profile profile, int effectFps = 20)
    {
        _backend = backend;
        Profile = profile;
        _fps = Math.Clamp(effectFps, 5, 60);
        _backend.Changed += OnBackendChanged;
    }

    public Profile Profile { get; private set; }
    public IDeviceBackend Backend => _backend;
    public bool EffectEngineRunning { get { lock (_runLock) return _engineTask is { IsCompleted: false }; } }

    /// <summary>Raised after settings were (re)applied or an apply failed. Argument is a user-facing message.</summary>
    public event Action<ApplyResult>? Applied;
    /// <summary>Raised when the device list or backend status changes.</summary>
    public event Action? DevicesChanged;

    private string[] _knownIds = Array.Empty<string>();

    private void OnBackendChanged()
    {
        DevicesChanged?.Invoke();
        // Restore saved settings for devices that just (re)appeared.
        var current = _backend.Devices.Select(d => d.Id).ToArray();
        var fresh = current.Except(_knownIds).ToArray();
        var gone = _knownIds.Except(current).ToArray();
        _knownIds = current;
        foreach (var id in gone) StopSoftware(id);
        if (fresh.Length > 0 && !_disposed)
            _ = Task.Run(async () =>
            {
                foreach (var id in fresh)
                {
                    var dev = _backend.Devices.FirstOrDefault(d => d.Id == id);
                    if (dev is not null && Profile.Devices.ContainsKey(id)) await ApplyDeviceAsync(dev);
                }
            });
    }

    public void SetFps(int fps) => _fps = Math.Clamp(fps, 5, 60);

    public async Task LoadProfileAsync(Profile profile)
    {
        Profile = profile;
        await ApplyAllAsync();
    }

    public async Task ApplyAllAsync()
    {
        foreach (var d in _backend.Devices.ToArray())
            if (Profile.Devices.ContainsKey(d.Id)) await ApplyDeviceAsync(d);
    }

    /// <summary>Settings of profile devices that are not currently present (kept, and applied when they return).</summary>
    public IReadOnlyList<KeyValuePair<string, DeviceSetting>> AbsentSettings() =>
        Profile.Devices.Where(kv => _backend.Devices.All(d => d.Id != kv.Key)).ToList();

    public DeviceSetting GetOrCreateSetting(DeviceInfo d)
    {
        if (!Profile.Devices.TryGetValue(d.Id, out var s))
        {
            s = new DeviceSetting { DeviceName = d.Name };
            // Seed from what the hardware currently shows so the first edit doesn't change colour unexpectedly.
            if (d.Colors.Count > 0) s.Color = d.Colors[0].ToHex();
            Profile.Devices[d.Id] = s;
        }
        s.DeviceName = d.Name;
        return s;
    }

    /// <summary>The effects this specific device can really do: Static, its own modes, and software effects if it has a Direct mode.</summary>
    public static IReadOnlyList<string> AvailableEffects(DeviceInfo d)
    {
        var list = new List<string>();
        if (CanStatic(d)) list.Add(EffectNames.Static);
        foreach (var m in d.Modes)
        {
            if (m.Name.Equals("Off", StringComparison.OrdinalIgnoreCase)) continue;
            if (m.Name.Equals("Direct", StringComparison.OrdinalIgnoreCase) || m.Name.Equals("Custom", StringComparison.OrdinalIgnoreCase)) continue;
            if (m.Name.Equals("Static", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(m.Name);
        }
        if (d.DirectMode is not null && d.LedCount > 0) list.AddRange(SoftwareEffects.All);
        return list;
    }

    private static ModeInfo? StaticMode(DeviceInfo d) =>
        d.Modes.FirstOrDefault(m => m.Name.Equals("Static", StringComparison.OrdinalIgnoreCase) && m.HasModeColor && m.ColorsMin <= 1 && m.ColorsMax >= 1);

    /// <summary>
    /// A hardware "Static" mode that takes its colours from the LEDs (per-LED colour) rather than from the mode itself.
    /// Verified on real hardware (ASRock Polychrome USB): such a board held the colour and drove addressable headers, while a
    /// single Direct-mode packet did not (Direct needs a continuous stream on some devices).
    /// </summary>
    private static ModeInfo? PerLedStaticMode(DeviceInfo d) =>
        d.Modes.FirstOrDefault(m => m.Name.Equals("Static", StringComparison.OrdinalIgnoreCase) && m.HasPerLed && !m.HasModeColor);

    public static bool CanStatic(DeviceInfo d) => d.DirectMode is not null || StaticMode(d) is not null || PerLedStaticMode(d) is not null;

    /// <summary>
    /// Puts the device in the mode used for one-shot colours: hardware Static (per-LED) when the device has it, else Direct.
    /// Software effects stream continuously and always use Direct.
    /// </summary>
    private async Task ActivateColourModeAsync(DeviceInfo d, CancellationToken ct)
    {
        var st = PerLedStaticMode(d);
        if (st is not null)
        {
            var m = st.Clone();
            if (m.HasBrightness) m.Brightness = m.BrightnessMax;   // we pre-scale the colours ourselves
            await _backend.SetModeAsync(d, d.Modes.IndexOf(st), m, ct);
            return;
        }
        await ActivateDirectAsync(d, ct);
    }

    /// <summary>Which controls make sense for the device's currently selected effect.</summary>
    public static (bool Speed, bool Brightness, bool Color) ControlsFor(DeviceInfo d, string effect)
    {
        if (SoftwareEffects.IsSoftware(effect)) return (true, true, effect is SoftwareEffects.Breathing or SoftwareEffects.Wave);
        if (effect == EffectNames.Static) return (false, true, true);
        var m = d.Modes.FirstOrDefault(x => x.Name.Equals(effect, StringComparison.OrdinalIgnoreCase));
        return m is null ? (false, false, false) : (m.HasSpeed, m.HasBrightness, m.HasModeColor || m.ColorMode == ColorMode.PerLed);
    }

    public async Task<ApplyResult> ApplyDeviceAsync(DeviceInfo d, CancellationToken ct = default)
    {
        await _applyLock.WaitAsync(ct);
        try
        {
            ApplyResult result;
            try { result = await ApplyCoreAsync(d, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or TimeoutException or ArgumentException or InvalidOperationException or ObjectDisposedException)
            {
                result = new(d.Id, false, $"{d.Name}: {ex.Message}");
            }
            Applied?.Invoke(result);
            return result;
        }
        finally { _applyLock.Release(); }
    }

    private async Task<ApplyResult> ApplyCoreAsync(DeviceInfo d, CancellationToken ct)
    {
        if (d.Controllability != Controllability.Controllable)
            return new(d.Id, false, $"{d.Name}: {d.WhyNotControllable}");

        var s = GetOrCreateSetting(d);
        StopSoftware(d.Id);
        d = await EnsureZoneSizesAsync(d, s, ct);
        if (d.LedCount == 0)
            return new(d.Id, false, $"{d.Name}: no LEDs are configured yet. Set the LED count of its addressable header(s) (Zones section) to the number of LEDs you have connected.");
        Rgb.TryParseHex(s.Color, out var color);
        double bright = s.Brightness / 100.0;

        if (SoftwareEffects.IsSoftware(s.Effect))
        {
            var direct = d.DirectMode;
            if (direct is null || d.LedCount == 0) return new(d.Id, false, $"{d.Name}: software effects need a per-LED (Direct) mode, which this device does not report.");
            if (!s.On) return await SolidAsync(d, Rgb.Black, ct);
            await ActivateDirectAsync(d, ct);
            StartSoftware(d, s.Effect, s.Speed, color, bright);
            return new(d.Id, true, $"{d.Name}: running {s.Effect}.");
        }

        if (s.Effect == EffectNames.Static)
        {
            if (!s.On) return await OffAsync(d, ct);
            var hasZones = s.Zones.Count > 0 && (d.DirectMode is not null || PerLedStaticMode(d) is not null) && d.Zones.Count > 0;
            if (hasZones) return await ZonedAsync(d, s, color, bright, ct);
            var sm = StaticMode(d);
            if (sm is not null)
            {
                var m = sm.Clone();
                if (m.HasBrightness) { m.Brightness = Lerp(m.BrightnessMin, m.BrightnessMax, s.Brightness); m.Colors = new() { color }; }
                else m.Colors = new() { color.Scale(bright) };
                await _backend.SetModeAsync(d, d.Modes.IndexOf(sm), m, ct);
                return new(d.Id, true, $"{d.Name}: static {color.ToHex()}.");
            }
            return await SolidAsync(d, color.Scale(bright), ct);
        }

        // Hardware effect reported by the device itself.
        var mode = d.Modes.FirstOrDefault(m => m.Name.Equals(s.Effect, StringComparison.OrdinalIgnoreCase));
        if (mode is null) return new(d.Id, false, $"{d.Name}: effect '{s.Effect}' is not supported by this device.");
        if (!s.On) return await OffAsync(d, ct);
        var hw = mode.Clone();
        if (hw.HasSpeed) hw.Speed = Lerp(hw.SpeedMin, hw.SpeedMax, s.Speed);
        if (hw.HasBrightness) hw.Brightness = Lerp(hw.BrightnessMin, hw.BrightnessMax, s.Brightness);
        if (hw.HasModeColor && hw.ColorMode == ColorMode.ModeSpecific)
        {
            if (hw.Colors.Count == 0) for (int i = 0; i < Math.Max(1, hw.ColorsMin); i++) hw.Colors.Add(color);
            else hw.Colors[0] = color;
        }
        await _backend.SetModeAsync(d, d.Modes.IndexOf(mode), hw, ct);
        // Per-LED-colour modes (e.g. Breathing on some mice) take their colour from the LED colours, not from the mode.
        if (hw.ColorMode == ColorMode.PerLed && d.LedCount > 0)
        {
            var frame = new Rgb[d.LedCount];
            Array.Fill(frame, color);
            await _backend.SetLedsAsync(d, frame, ct);
        }
        return new(d.Id, true, $"{d.Name}: {s.Effect}.");
    }

    /// <summary>
    /// Applies saved LED counts to resizable zones (addressable headers). The backend's device list changes when a zone is
    /// resized, so we rescan and continue with the fresh device object.
    /// </summary>
    private async Task<DeviceInfo> EnsureZoneSizesAsync(DeviceInfo d, DeviceSetting s, CancellationToken ct)
    {
        if (s.ZoneSizes.Count == 0) return d;
        bool changed = false;
        for (int i = 0; i < d.Zones.Count; i++)
        {
            var z = d.Zones[i];
            if (!z.Resizable || !s.ZoneSizes.TryGetValue(z.Name, out var want)) continue;
            want = (int)Math.Clamp(want, z.LedsMin, z.LedsMax);
            if (want == z.LedsCount) continue;
            await _backend.ResizeZoneAsync(d, i, want, ct);
            changed = true;
        }
        if (!changed) return d;
        await _backend.RescanAsync(ct);
        return _backend.Devices.FirstOrDefault(x => x.Id == d.Id) ?? d;
    }

    /// <summary>
    /// Lights a zone for the LED-count wizard: the zone is first sized to its maximum, then either every LED
    /// (<paramref name="onlyLed"/> null) or exactly one LED is lit; other zones are blanked. Returns the refreshed device.
    /// </summary>
    public async Task<DeviceInfo> ProbeZoneAsync(DeviceInfo d, int zoneIndex, int? onlyLed, Rgb color, CancellationToken ct = default, int? firstN = null)
    {
        await _applyLock.WaitAsync(ct);
        try
        {
            StopSoftware(d.Id);
            var z = d.Zones[zoneIndex];
            if (z.Resizable && z.LedsCount != z.LedsMax)
            {
                await _backend.ResizeZoneAsync(d, zoneIndex, (int)z.LedsMax, ct);
                await _backend.RescanAsync(ct);
                d = _backend.Devices.FirstOrDefault(x => x.Id == d.Id) ?? d;
            }
            await ActivateColourModeAsync(d, ct);
            for (int i = 0; i < d.Zones.Count; i++)
            {
                var zone = d.Zones[i];
                if (zone.LedsCount == 0) continue;
                var frame = new Rgb[zone.LedsCount];
                if (i == zoneIndex)
                {
                    if (firstN is { } n) { for (int k = 0; k < Math.Min(n, frame.Length); k++) frame[k] = color; }
                    else if (onlyLed is { } k) { if (k >= 0 && k < frame.Length) frame[k] = color; }
                    else Array.Fill(frame, color);
                }
                await _backend.SetZoneLedsAsync(d, i, frame, ct);
            }
            return d;
        }
        finally { _applyLock.Release(); }
    }

    /// <summary>True while an "identify" view is showing (everything but the target is dark).</summary>
    public bool SoloActive { get; private set; }

    /// <summary>
    /// Visual-clarity mode for testing: every other device is turned dark, and on the target device either one zone or the whole
    /// device is lit in <paramref name="color"/> while its other zones stay dark. Profile settings are NOT changed;
    /// <see cref="EndSoloAsync"/> restores the saved lighting.
    /// </summary>
    public async Task SoloAsync(DeviceInfo target, int? zoneIndex, Rgb color, CancellationToken ct = default)
    {
        await StopEffectsAsync();
        // Make sure every controllable device has a saved setting so that EndSolo can restore it.
        foreach (var dev in _backend.Devices.Where(x => x.Controllability == Controllability.Controllable)) GetOrCreateSetting(dev);

        await _applyLock.WaitAsync(ct);
        try
        {
            SoloActive = true;
            foreach (var dev in _backend.Devices.Where(x => x.Controllability == Controllability.Controllable).ToArray())
            {
                try
                {
                    if (dev.Id != target.Id) { await SolidAsync(dev, Rgb.Black, ct); continue; }
                    if (zoneIndex is null) { await SolidAsync(dev, color, ct); continue; }
                    await ActivateColourModeAsync(dev, ct);
                    for (int i = 0; i < dev.Zones.Count; i++)
                    {
                        var z = dev.Zones[i];
                        if (z.LedsCount == 0) continue;
                        await _backend.SetZoneLedsAsync(dev, i, Enumerable.Repeat(i == zoneIndex ? color : Rgb.Black, (int)z.LedsCount).ToArray(), ct);
                    }
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or ArgumentException or InvalidOperationException)
                {
                    Applied?.Invoke(new(dev.Id, false, $"{dev.Name}: identify failed ({ex.Message})."));
                }
            }
        }
        finally { _applyLock.Release(); }
    }

    /// <summary>Leaves identify mode and re-applies the saved lighting of every device.</summary>
    public async Task EndSoloAsync(CancellationToken ct = default)
    {
        SoloActive = false;
        await ApplyAllAsync();
    }

    private static uint Lerp(uint a, uint b, int pct) => (uint)Math.Round(a + (b - (double)a) * Math.Clamp(pct, 0, 100) / 100.0);

    private async Task ActivateDirectAsync(DeviceInfo d, CancellationToken ct)
    {
        var direct = d.DirectMode!;
        var idx = d.Modes.IndexOf(direct);
        if (d.ActiveMode != idx) await _backend.SetModeAsync(d, idx, direct, ct);
    }

    private async Task<ApplyResult> SolidAsync(DeviceInfo d, Rgb c, CancellationToken ct)
    {
        if (d.DirectMode is null && PerLedStaticMode(d) is null) return new(d.Id, false, $"{d.Name}: no static or direct mode available.");
        await ActivateColourModeAsync(d, ct);
        var frame = new Rgb[d.LedCount];
        Array.Fill(frame, c);
        await _backend.SetLedsAsync(d, frame, ct);
        return new(d.Id, true, $"{d.Name}: {c.ToHex()}.");
    }

    private async Task<ApplyResult> OffAsync(DeviceInfo d, CancellationToken ct)
    {
        // Black through the colour path first: on the ASRock board the device's own "Off" mode did not darken fans on its headers,
        // whereas black colour data did. Fall back to the device's Off mode only when there is no colour path.
        if (d.DirectMode is not null || PerLedStaticMode(d) is not null)
        {
            var r = await SolidAsync(d, Rgb.Black, ct);
            return r.Ok ? new(d.Id, true, $"{d.Name}: off.") : r;
        }
        var off = d.Modes.FirstOrDefault(m => m.Name.Equals("Off", StringComparison.OrdinalIgnoreCase));
        if (off is not null) { await _backend.SetModeAsync(d, d.Modes.IndexOf(off), off.Clone(), ct); return new(d.Id, true, $"{d.Name}: off."); }
        return new(d.Id, false, $"{d.Name}: no way to turn this device off.");
    }

    private async Task<ApplyResult> ZonedAsync(DeviceInfo d, DeviceSetting s, Rgb baseColor, double bright, CancellationToken ct)
    {
        await ActivateColourModeAsync(d, ct);
        for (int zi = 0; zi < d.Zones.Count; zi++)
        {
            var z = d.Zones[zi];
            var c = s.Zones.TryGetValue(z.Name, out var hex) && Rgb.TryParseHex(hex, out var zc) ? zc : baseColor;
            var frame = new Rgb[z.LedsCount];
            Array.Fill(frame, c.Scale(bright));
            if (frame.Length > 0) await _backend.SetZoneLedsAsync(d, zi, frame, ct);
        }
        return new(d.Id, true, $"{d.Name}: zones updated.");
    }

    // ---- software effect engine ----------------------------------------------------------------
    private sealed class SoftwareRun
    {
        public required DeviceInfo Device;
        public required string Effect;
        public int Speed;
        public Rgb Color;
        public double Brightness;
        public Rgb[]? Last;
        public Rgb[] Frame = Array.Empty<Rgb>();
    }

    private void StartSoftware(DeviceInfo d, string effect, int speed, Rgb color, double bright)
    {
        lock (_runLock)
        {
            _running[d.Id] = new SoftwareRun { Device = d, Effect = effect, Speed = speed, Color = color, Brightness = bright, Frame = new Rgb[d.LedCount] };
            if (_engineTask is not { IsCompleted: false } && !_disposed)
            {
                _engineCts = new CancellationTokenSource();
                var token = _engineCts.Token;
                _engineTask = Task.Run(() => EngineLoopAsync(token));
            }
        }
    }

    private void StopSoftware(string deviceId)
    {
        lock (_runLock) _running.Remove(deviceId);
    }

    private async Task EngineLoopAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / _fps));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                SoftwareRun[] runs;
                lock (_runLock)
                {
                    runs = _running.Values.ToArray();
                    if (runs.Length == 0) { _engineTask = null; return; } // nothing to animate: the timer dies with the task
                }
                double t = sw.Elapsed.TotalSeconds;
                foreach (var r in runs)
                {
                    SoftwareEffects.Render(r.Effect, t, r.Speed, r.Color, r.Brightness, r.Frame);
                    if (r.Last is not null && r.Last.AsSpan().SequenceEqual(r.Frame)) continue; // no change, no USB traffic
                    // Send the frame, then keep it as "last" and render the next one into the old buffer (no per-frame clone).
                    var sent = r.Frame;
                    r.Frame = r.Last ?? new Rgb[sent.Length];
                    r.Last = sent;
                    try { await _backend.SetLedsAsync(r.Device, sent, ct); }
                    catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
                    {
                        StopSoftware(r.Device.Id);
                        Applied?.Invoke(new(r.Device.Id, false, $"{r.Device.Name}: effect stopped ({ex.Message})."));
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task StopEffectsAsync()
    {
        Task? t; CancellationTokenSource? cts;
        lock (_runLock) { _running.Clear(); t = _engineTask; cts = _engineCts; _engineTask = null; _engineCts = null; }
        try { cts?.Cancel(); } catch { }
        if (t is not null) { try { await t.WaitAsync(TimeSpan.FromSeconds(2)); } catch { } }
        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Changed -= OnBackendChanged;
        await StopEffectsAsync();
    }
}
