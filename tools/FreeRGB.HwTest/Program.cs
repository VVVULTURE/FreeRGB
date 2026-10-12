using FreeRGB.Core;
using FreeRGB.Core.Backends;

// Real-hardware sequence through FreeRGB's own LightingService. Restores each device's original mode/colours at the end.
int hold = args.Length > 0 ? int.Parse(args[0]) : 4;
string filter = args.Length > 1 ? args[1] : "";
var keys = args.Skip(2).ToHashSet();
bool Want(string k) => keys.Count == 0 || keys.Contains(k);
await using var backend = new OpenRgbBackend();
await backend.ConnectAsync();
Console.WriteLine($"{backend.Status.State}: {backend.Status.Message}");
var devices = backend.Devices.Where(d => d.Controllability == Controllability.Controllable && d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
await using var svc = new LightingService(backend, new Profile { Name = "hwtest" });

// 1) remember the initial state
var initial = devices.ToDictionary(d => d.Id, d => (Mode: d.ActiveMode, Colors: d.Colors.ToList(), Name: d.Modes[d.ActiveMode].Name));
foreach (var d in devices) Console.WriteLine($"INITIAL {d.Name}: mode='{initial[d.Id].Name}' firstLed={(d.Colors.Count > 0 ? d.Colors[0].ToHex() : "-")} leds={d.LedCount}");

async Task Step(string label, Action<DeviceSetting, DeviceInfo> set)
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] STEP {label}");
    foreach (var d in devices)
    {
        var s = svc.GetOrCreateSetting(d);
        set(s, d);
        var r = await svc.ApplyDeviceAsync(d);
        Console.WriteLine($"   {(r.Ok ? "OK  " : "FAIL")} {r.Message}");
    }
    await Task.Delay(hold * 1000);
}

try
{
    if (Want("red")) await Step("static RED 100%",   (s, d) => { s.On = true; s.Effect = "Static"; s.Color = "#FF0000"; s.Brightness = 100; });
    if (Want("green")) await Step("static GREEN 100%", (s, d) => { s.Color = "#00FF00"; });
    if (Want("blue")) await Step("static BLUE 100%",  (s, d) => { s.Color = "#0000FF"; });
    if (Want("dim")) await Step("static WHITE 25% brightness", (s, d) => { s.Color = "#FFFFFF"; s.Brightness = 25; });
    if (Want("off")) await Step("ON: solid WHITE for 3s, then (next step) OFF", (s, d) => { s.On = true; s.Effect = "Static"; s.Color = "#FFFFFF"; s.Brightness = 100; });
    if (Want("off")) await Step("OFF", (s, d) => { s.On = false; });
    if (Want("orange")) await Step("ON again, ORANGE 100%", (s, d) => { s.On = true; s.Color = "#FF8000"; s.Brightness = 100; });
    if (Want("zones")) foreach (var d in devices.Where(d => d.Zones.Count > 1))
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] STEP per-zone colours on {d.Name}: " + string.Join(", ", d.Zones.Select(z => $"{z.Name}({z.LedsCount})")));
        var s = svc.GetOrCreateSetting(d); s.Effect = "Static";
        var palette = new[] { "#FF0000", "#00FF00", "#0000FF", "#FFFF00" };
        s.Zones.Clear();
        for (int i = 0; i < d.Zones.Count; i++) s.Zones[d.Zones[i].Name] = palette[i % palette.Length];
        var r = await svc.ApplyDeviceAsync(d); Console.WriteLine($"   {(r.Ok ? "OK  " : "FAIL")} {r.Message}");
        await Task.Delay(hold * 1000);
        s.Zones.Clear();
    }
    // hardware effects: pick each device's own Spectrum Cycle / Breathing, speed 50%
    if (Want("spectrum")) await Step("hardware effect 'Spectrum Cycle' speed 50%", (s, d) =>
    {
        s.Effect = d.Modes.Any(m => m.Name == "Spectrum Cycle") ? "Spectrum Cycle" : "Static"; s.Speed = 50; s.Brightness = 100; });
    if (Want("breathing")) await Step("hardware effect 'Breathing' red speed 50%", (s, d) =>
    {
        s.Effect = d.Modes.Any(m => m.Name == "Breathing") ? "Breathing" : "Static"; s.Color = "#FF0000"; s.Speed = 50; });
    if (Want("software")) await Step("SOFTWARE effect Rainbow (per-LED frames @20fps) for 2x hold", (s, d) =>
    {
        s.Effect = d.DirectMode is not null ? SoftwareEffects.Rainbow : "Static"; s.Speed = 60; s.Brightness = 100; });
    await Task.Delay(hold * 1000);
}
finally
{
    // 2) restore: stop effects, put each device back into its original mode and colours
    await svc.StopEffectsAsync();
    foreach (var d in devices)
    {
        var (mode, colors, name) = initial[d.Id];
        try
        {
            var m = d.Modes[mode];
            await backend.SetModeAsync(d, mode, m);
            if (m.HasPerLed || m.Name is "Direct" or "Custom" or "Static")
                if (colors.Count == d.LedCount) await backend.SetLedsAsync(d, colors);
            Console.WriteLine($"RESTORED {d.Name}: mode '{name}'");
        }
        catch (Exception ex) { Console.WriteLine($"RESTORE FAILED {d.Name}: {ex.Message}"); }
    }
}
