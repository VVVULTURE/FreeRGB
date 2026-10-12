using System.Buffers.Binary;
using FreeRGB.Core;
using FreeRGB.Core.Backends;

namespace FreeRGB.Tests;

public class RgbTests
{
    [Theory]
    [InlineData("#FF8000", 255, 128, 0)]
    [InlineData("00ff7f", 0, 255, 127)]
    public void ParsesHex(string text, int r, int g, int b)
    {
        Assert.True(Rgb.TryParseHex(text, out var c));
        Assert.Equal(new Rgb((byte)r, (byte)g, (byte)b), c);
    }

    [Theory]
    [InlineData("")] [InlineData(null)] [InlineData("#12345")] [InlineData("#GG0000")] [InlineData("#1234567")]
    public void RejectsBadHex(string? text) => Assert.False(Rgb.TryParseHex(text, out _));

    [Fact] public void HsvPrimaries()
    {
        Assert.Equal(new Rgb(255, 0, 0), Rgb.FromHsv(0, 1, 1));
        Assert.Equal(new Rgb(0, 255, 0), Rgb.FromHsv(120, 1, 1));
        Assert.Equal(new Rgb(0, 0, 255), Rgb.FromHsv(240, 1, 1));
        Assert.Equal(Rgb.Black, Rgb.FromHsv(77, 1, 0));
    }

    [Fact] public void ScaleClamps() => Assert.Equal(new Rgb(100, 50, 0), new Rgb(100, 50, 0).Scale(5));
}

public class ProtocolTests
{
    [Fact]
    public void HeaderRoundTrip()
    {
        var p = OpenRgbProtocol.BuildPacket(3, 1050, new byte[] { 1, 2, 3 });
        Assert.Equal("ORGB", System.Text.Encoding.ASCII.GetString(p, 0, 4));
        Assert.True(OpenRgbProtocol.TryParseHeader(p, out var dev, out var pkt, out var size));
        Assert.Equal((3u, 1050u, 3u), (dev, pkt, size));
    }

    [Fact] public void RejectsBadMagicAndHugeSize()
    {
        var bad = OpenRgbProtocol.BuildPacket(0, 0, ReadOnlySpan<byte>.Empty); bad[0] = (byte)'X';
        Assert.False(OpenRgbProtocol.TryParseHeader(bad, out _, out _, out _));
        var huge = OpenRgbProtocol.BuildPacket(0, 0, ReadOnlySpan<byte>.Empty);
        BinaryPrimitives.WriteUInt32LittleEndian(huge.AsSpan(12), uint.MaxValue);
        Assert.False(OpenRgbProtocol.TryParseHeader(huge, out _, out _, out _));
    }

    [Fact] public void UpdateLedsLayout()
    {
        var b = OpenRgbProtocol.BuildUpdateLeds(new[] { new Rgb(1, 2, 3), new Rgb(4, 5, 6) });
        Assert.Equal(4u + 2 + 8, BinaryPrimitives.ReadUInt32LittleEndian(b));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4)));
        Assert.Equal(new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 }, b[6..]);
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(5)]
    public void DeviceRoundTripAcrossProtocols(int proto)
    {
        var src = FakeOpenRgbServer.SampleStripWithStatic();
        var wire = FakeOpenRgbServer.SerializeDevice(src, proto);
        var got = OpenRgbProtocol.ParseDevice(wire, proto);
        Assert.Equal(src.Name, got.Name);
        Assert.Equal(src.Modes.Count, got.Modes.Count);
        Assert.Equal(src.LedCount, got.LedCount);
        Assert.Equal(src.Zones[0].LedsCount, got.Zones[0].LedsCount);
        Assert.Equal(proto >= 3 ? 100u : 0u, got.Modes[1].BrightnessMax);
        Assert.Equal("Static", got.Modes[1].Name);
    }

    [Fact] public void TruncatedDeviceThrowsInsteadOfCrashing()
    {
        var wire = FakeOpenRgbServer.SerializeDevice(FakeOpenRgbServer.SampleStripWithStatic(), 5);
        Assert.Throws<InvalidDataException>(() => OpenRgbProtocol.ParseDevice(wire.AsSpan(0, wire.Length / 2), 5));
    }
}

public class BackendTests
{
    [Fact]
    public async Task UnavailableWhenNothingListening()
    {
        // Port 1 on loopback is closed.
        await using var b = new OpenRgbBackend("127.0.0.1", 1, TimeSpan.FromMilliseconds(500));
        await b.ConnectAsync();
        Assert.Equal(BackendState.Unavailable, b.Status.State);
        Assert.Empty(b.Devices);
        Assert.Contains("OpenRGB", b.Status.Message);
    }

    [Fact]
    public async Task EmptyDeviceListIsConnectedNotError()
    {
        await using var srv = new FakeOpenRgbServer().Start();
        await using var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        Assert.Equal(BackendState.Connected, b.Status.State);
        Assert.Empty(b.Devices);
    }

    [Fact]
    public async Task EnumeratesAndNegotiatesDown()
    {
        await using var srv = new FakeOpenRgbServer { ServerProtocol = 3 }.Start();
        srv.Devices.Add(FakeOpenRgbServer.SampleStripWithStatic());
        await using var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        Assert.Equal(3, b.NegotiatedProtocol);
        var d = Assert.Single(b.Devices);
        Assert.Equal("Test Strip", d.Name);
        Assert.Equal(Controllability.Controllable, d.Controllability);
    }

    [Fact]
    public async Task ServerThatNeverAnswersVersionIsProtocolZero()
    {
        await using var srv = new FakeOpenRgbServer { ServerProtocol = 0, AnswerVersion = false }.Start();
        srv.Devices.Add(FakeOpenRgbServer.SampleStripWithStatic());
        await using var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        Assert.Equal(0, b.NegotiatedProtocol);
        Assert.Single(b.Devices);
    }

    [Fact]
    public async Task DisconnectIsReportedAndReconnectWorks()
    {
        await using var srv = new FakeOpenRgbServer().Start();
        srv.Devices.Add(FakeOpenRgbServer.SampleStripWithStatic());
        await using var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        srv.DropClients();
        await WaitFor(() => b.Status.State != BackendState.Connected);
        Assert.Empty(b.Devices);
        await b.ConnectAsync();
        Assert.Equal(BackendState.Connected, b.Status.State);
        Assert.Single(b.Devices);
    }

    [Fact]
    public async Task DeviceListUpdatedTriggersRescan()
    {
        await using var srv = new FakeOpenRgbServer().Start();
        await using var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        srv.Devices.Add(FakeOpenRgbServer.SampleStripWithStatic());
        await srv.NotifyDeviceListUpdatedAsync();
        await WaitFor(() => b.Devices.Count == 1);
        Assert.Single(b.Devices);
    }

    [Fact]
    public async Task RejectsOutOfRangeModeValues()
    {
        await using var srv = new FakeOpenRgbServer().Start();
        srv.Devices.Add(FakeOpenRgbServer.SampleStripWithStatic());
        await using var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        var d = b.Devices[0];
        var m = d.Modes[1].Clone(); m.Brightness = 500;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.SetModeAsync(d, 1, m));
    }

    internal static async Task WaitFor(Func<bool> cond, int ms = 3000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond() && DateTime.UtcNow < end) await Task.Delay(20);
        Assert.True(cond(), "condition not reached in time");
    }
}

public class LightingTests
{
    private static async Task<(FakeOpenRgbServer, OpenRgbBackend, LightingService)> Setup(params DeviceInfo[] devices)
    {
        var srv = new FakeOpenRgbServer().Start();
        srv.Devices.AddRange(devices);
        var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        return (srv, b, new LightingService(b, new Profile()));
    }

    [Fact]
    public async Task StaticColorUsesStaticModeWithBrightness()
    {
        var (srv, b, svc) = await Setup(FakeOpenRgbServer.SampleStripWithStatic());
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        var s = svc.GetOrCreateSetting(d);
        s.Color = "#FF0000"; s.Brightness = 50;
        var r = await svc.ApplyDeviceAsync(d);
        Assert.True(r.Ok, r.Message);
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Pkt == OpenRgbProtocol.UpdateMode));
        var upd = srv.Snapshot().Last(p => p.Pkt == OpenRgbProtocol.UpdateMode).Payload;
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(upd.AsSpan(4))); // mode index 1 = Static
        // The colour is the last 4 bytes of the Mode Data: R,G,B,0 (brightness is carried separately)
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, upd[^4..]);
    }

    [Fact]
    public async Task OffOnDirectOnlyDeviceSendsBlack()
    {
        var dev = FakeOpenRgbServer.SampleStripWithStatic();
        dev.Modes.RemoveAll(m => m.Name == "Static");
        var (srv, b, svc) = await Setup(dev);
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        svc.GetOrCreateSetting(d).On = false;
        var r = await svc.ApplyDeviceAsync(d);
        Assert.True(r.Ok, r.Message);
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Pkt == OpenRgbProtocol.UpdateLeds));
        var leds = srv.Snapshot().Last(p => p.Pkt == OpenRgbProtocol.UpdateLeds).Payload;
        Assert.All(leds.Skip(6), x => Assert.Equal(0, x));
    }

    [Fact]
    public async Task HardwareEffectMapsSpeedOntoDeviceRange()
    {
        var (srv, b, svc) = await Setup(FakeOpenRgbServer.SampleStripWithStatic());
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        var s = svc.GetOrCreateSetting(d); s.Effect = "Rainbow Wave"; s.Speed = 100; // device range 10 (slow) .. 2 (fast)
        Assert.True((await svc.ApplyDeviceAsync(d)).Ok);
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Pkt == OpenRgbProtocol.UpdateMode));
        var payload = srv.Snapshot().Last(p => p.Pkt == OpenRgbProtocol.UpdateMode).Payload;
        var mode = payload.AsSpan(8);
        // name "Rainbow Wave\0" = 2+13 bytes, value(4), flags(4), speedMin(4), speedMax(4), brMin(4), brMax(4), colMin(4), colMax(4), speed(4)
        int speedOffset = 2 + 13 + 4 + 4 + 4 + 4 + 4 + 4 + 4 + 4;
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(mode[speedOffset..]));
    }

    [Fact]
    public async Task PerLedStaticModeIsPreferredOverSingleShotDirect()
    {
        // Mirrors the ASRock Polychrome USB board: Direct needs a stream, hardware Static (per-LED colour) holds the colour.
        var dev = FakeOpenRgbServer.SampleStripWithStatic();
        dev.Modes.RemoveAll(m => m.Name == "Static");
        dev.Modes.Add(new ModeInfo { Name = "Static", Flags = ModeFlags.HasPerLedColor, ColorMode = ColorMode.PerLed });
        var (srv, b, svc) = await Setup(dev);
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        svc.GetOrCreateSetting(d).Color = "#00FF00";
        Assert.True((await svc.ApplyDeviceAsync(d)).Ok);
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Pkt == OpenRgbProtocol.UpdateLeds));
        var modes = srv.Snapshot().Where(p => p.Pkt == OpenRgbProtocol.UpdateMode).ToArray();
        Assert.Single(modes);
        Assert.Equal(d.Modes.FindIndex(m => m.Name == "Static"), BinaryPrimitives.ReadInt32LittleEndian(modes[0].Payload.AsSpan(4)));
        // and "off" goes through the same colour path (black), not the device's own Off mode
        svc.GetOrCreateSetting(d).On = false;
        Assert.True((await svc.ApplyDeviceAsync(d)).Ok);
        await BackendTests.WaitFor(() => srv.Snapshot().Count(p => p.Pkt == OpenRgbProtocol.UpdateLeds) >= 2);
        var leds = srv.Snapshot().Last(p => p.Pkt == OpenRgbProtocol.UpdateLeds).Payload;
        Assert.All(leds.Skip(6), x => Assert.Equal(0, x));
    }

    [Fact]
    public async Task PerLedColourEffectSendsColourViaLeds()
    {
        var dev = FakeOpenRgbServer.SampleStripWithStatic();
        dev.Modes.Add(new ModeInfo { Name = "Breathing", Flags = ModeFlags.HasSpeed | ModeFlags.HasPerLedColor, SpeedMin = 200, SpeedMax = 10, Speed = 100, ColorMode = ColorMode.PerLed });
        var (srv, b, svc) = await Setup(dev);
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        var st = svc.GetOrCreateSetting(d); st.Effect = "Breathing"; st.Color = "#FF0000";
        Assert.True((await svc.ApplyDeviceAsync(d)).Ok);
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Pkt == OpenRgbProtocol.UpdateLeds));
        var leds = srv.Snapshot().Last(p => p.Pkt == OpenRgbProtocol.UpdateLeds).Payload;
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, leds[6..10]);
        Assert.True(LightingService.ControlsFor(d, "Breathing").Color);
    }

    [Fact]
    public async Task SavedZoneSizeResizesAddressableHeaderThenAppliesColour()
    {
        var (srv, b, svc) = await Setup(FakeOpenRgbServer.SampleBoardWithAddressableHeader());
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        Assert.Equal(0, d.LedCount);
        Assert.True(d.Zones[0].Resizable);
        var st = svc.GetOrCreateSetting(d); st.Color = "#00FF00"; st.ZoneSizes["Addressable Header 1"] = 24;
        var r = await svc.ApplyDeviceAsync(d);
        Assert.True(r.Ok, r.Message);
        Assert.Contains(srv.Snapshot(), p => p.Pkt == OpenRgbProtocol.ResizeZone);
        Assert.Equal(24, b.Devices[0].LedCount);
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Pkt == OpenRgbProtocol.UpdateLeds));
        var leds = srv.Snapshot().Last(p => p.Pkt == OpenRgbProtocol.UpdateLeds).Payload;
        Assert.Equal(24, BinaryPrimitives.ReadUInt16LittleEndian(leds.AsSpan(4)));
    }

    [Fact]
    public async Task ResizeOutsideDeviceRangeIsRejected()
    {
        var (srv, b, svc) = await Setup(FakeOpenRgbServer.SampleBoardWithAddressableHeader());
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => b.ResizeZoneAsync(b.Devices[0], 0, 5000));
        var fixedZone = FakeOpenRgbServer.SampleStripWithStatic(); fixedZone.Zones[0].LedsMin = fixedZone.Zones[0].LedsMax;
        await Assert.ThrowsAsync<ArgumentException>(() => b.ResizeZoneAsync(fixedZone, 0, 3));
    }

    [Fact]
    public async Task UnsupportedEffectIsRejectedNotSent()
    {
        var (srv, b, svc) = await Setup(FakeOpenRgbServer.SampleStripWithStatic());
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        svc.GetOrCreateSetting(d).Effect = "Does Not Exist";
        var r = await svc.ApplyDeviceAsync(d);
        Assert.False(r.Ok);
        Assert.DoesNotContain(srv.Snapshot(), p => p.Pkt == OpenRgbProtocol.UpdateMode);
    }

    [Fact]
    public async Task DeviceWithNoLedsIsDetectedButNotControllable()
    {
        var dev = new DeviceInfo { Name = "Ghost", Serial = "1", Location = "x", Type = DeviceType.Unknown };
        dev.Id = DeviceInfo.MakeId(dev.Name, dev.Serial, dev.Location);
        var (srv, b, svc) = await Setup(dev);
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        Assert.Equal(Controllability.DetectedNotControllable, d.Controllability);
        var r = await svc.ApplyDeviceAsync(d);
        Assert.False(r.Ok);
        Assert.Contains("no LEDs", r.Message);
    }

    [Fact]
    public async Task SoftwareEffectRunsThenStopsEngine()
    {
        var (srv, b, svc) = await Setup(FakeOpenRgbServer.SampleStripWithStatic());
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        var d = b.Devices[0];
        var st = svc.GetOrCreateSetting(d); st.Effect = SoftwareEffects.Rainbow;
        Assert.True((await svc.ApplyDeviceAsync(d)).Ok);
        await BackendTests.WaitFor(() => srv.Snapshot().Count(p => p.Pkt == OpenRgbProtocol.UpdateLeds) >= 3);
        Assert.True(svc.EffectEngineRunning);
        st.Effect = EffectNames.Static;
        await svc.ApplyDeviceAsync(d);
        await BackendTests.WaitFor(() => !svc.EffectEngineRunning);
    }

    [Fact]
    public async Task AbsentDeviceSettingsArePreservedAndAppliedOnReturn()
    {
        var dev = FakeOpenRgbServer.SampleStripWithStatic();
        var (srv, b, svc) = await Setup();
        await using var _s = srv; await using var _b = b; await using var _l = svc;
        svc.Profile.Devices[dev.Id] = new DeviceSetting { Color = "#00FF00", DeviceName = dev.Name };
        Assert.Single(svc.AbsentSettings());
        srv.Devices.Add(dev);
        await srv.NotifyDeviceListUpdatedAsync();
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Pkt == OpenRgbProtocol.UpdateMode));
        Assert.Empty(svc.AbsentSettings());
    }

    [Fact]
    public void AvailableEffectsReflectRealCapabilities()
    {
        var d = FakeOpenRgbServer.SampleStripWithStatic();
        var fx = LightingService.AvailableEffects(d);
        Assert.Contains("Static", fx); Assert.Contains("Rainbow Wave", fx); Assert.Contains(SoftwareEffects.Rainbow, fx);
        Assert.DoesNotContain("Direct", fx);
        d.Modes.RemoveAll(m => m.Name == "Direct");
        Assert.DoesNotContain(SoftwareEffects.Rainbow, LightingService.AvailableEffects(d));
    }
}

public class SoloTests
{
    [Fact]
    public async Task SoloDarkensOtherDevicesAndOtherZonesThenRestores()
    {
        var a = FakeOpenRgbServer.SampleStripWithStatic("Strip A", "A", 4);
        var other = FakeOpenRgbServer.SampleStripWithStatic("Strip B", "B", 4);
        var srv = new FakeOpenRgbServer().Start();
        srv.Devices.Add(a); srv.Devices.Add(other);
        await using var _s = srv;
        await using var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        await using var svc = new LightingService(b, new Profile());
        svc.GetOrCreateSetting(b.Devices[1]).Color = "#0000FF";   // device B has a saved blue
        await svc.SoloAsync(b.Devices[0], null, new Rgb(255, 0, 0));
        Assert.True(svc.SoloActive);
        // Device B (handle 1) must have received black data, device A (handle 0) red.
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Dev == 1 && p.Pkt == OpenRgbProtocol.UpdateLeds)
                                       && srv.Snapshot().Any(p => p.Dev == 0 && p.Pkt == OpenRgbProtocol.UpdateLeds));
        var toB = srv.Snapshot().Last(p => p.Dev == 1 && p.Pkt == OpenRgbProtocol.UpdateLeds).Payload;
        Assert.All(toB.Skip(6), x => Assert.Equal(0, x));                       // other device: all black
        var toA = srv.Snapshot().Last(p => p.Dev == 0 && p.Pkt == OpenRgbProtocol.UpdateLeds).Payload;
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, toA[6..10]);                   // target: red
        await svc.EndSoloAsync();
        Assert.False(svc.SoloActive);
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Dev == 1 && p.Pkt == OpenRgbProtocol.UpdateMode));
        var restoredB = srv.Snapshot().Last(p => p.Dev == 1 && p.Pkt == OpenRgbProtocol.UpdateMode).Payload;
        Assert.Equal(new byte[] { 0, 0, 255, 0 }, restoredB[^4..]);   // saved blue is back
    }
}

public class LedCountSearchTests
{
    [Fact]
    public void FindsEveryCountFrom0To100WithinSevenQuestions()
    {
        for (int count = 0; count <= 100; count++)
        {
            var search = new LedCountSearch(100);
            int questions = 0;
            while (!search.Done)
            {
                var k = search.NextProbe;
                Assert.InRange(k, 0, 99);              // never asks about a non-existent index
                search.Answer(visible: k < count);     // LED k is visible exactly when k < count
                questions++;
            }
            Assert.Equal(count, search.Result);
            Assert.True(questions <= 7, $"count {count} needed {questions} questions");
        }
    }

    [Fact]
    public async Task ProbeLightsExactlyOneLedAndSizesZoneToMax()
    {
        var srv = new FakeOpenRgbServer().Start();
        srv.Devices.Add(FakeOpenRgbServer.SampleBoardWithAddressableHeader());
        await using var _s = srv;
        await using var b = new OpenRgbBackend("127.0.0.1", srv.Port);
        await b.ConnectAsync();
        await using var svc = new LightingService(b, new Profile());
        var d = await svc.ProbeZoneAsync(b.Devices[0], 0, onlyLed: 3, new Rgb(255, 255, 255));
        Assert.Equal(100u, d.Zones[0].LedsCount);
        await BackendTests.WaitFor(() => srv.Snapshot().Any(p => p.Pkt == OpenRgbProtocol.UpdateZoneLeds));
        var payload = srv.Snapshot().Last(p => p.Pkt == OpenRgbProtocol.UpdateZoneLeds).Payload;
        var colours = payload.AsSpan(10);              // size(4) zone(4) count(2)
        Assert.Equal(400, colours.Length);
        for (int i = 0; i < 100; i++) Assert.Equal(i == 3 ? 255 : 0, colours[i * 4]);
    }
}

public class ProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "freergb-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Profile Sample(string name = "Gaming") => new()
    {
        Name = name,
        Devices = { ["Dev|1|loc"] = new DeviceSetting { Color = "#112233", Brightness = 80, Effect = "Static", Speed = 20, DeviceName = "Dev" } },
    };

    [Fact] public void SaveLoadRoundTrip()
    {
        var store = new ProfileStore(_dir);
        store.Save(Sample());
        var p = store.Load("Gaming");
        Assert.Equal("#112233", p.Devices["Dev|1|loc"].Color);
        Assert.Equal(new[] { "Gaming" }, store.List());
    }

    [Fact] public void ExportThenImportElsewhere()
    {
        var a = new ProfileStore(_dir + "-a"); var b = new ProfileStore(_dir + "-b");
        try
        {
            a.Save(Sample());
            var file = Path.Combine(_dir + "-a", "out.json.export");
            a.Export("Gaming", file);
            var imported = b.Import(file);
            Assert.Equal("Gaming", imported.Name);
            Assert.Throws<ProfileValidationException>(() => b.Import(file)); // no silent overwrite
            Assert.Equal("Gaming", b.Import(file, overwrite: true).Name);
        }
        finally { try { Directory.Delete(_dir + "-a", true); Directory.Delete(_dir + "-b", true); } catch { } }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"version\":99,\"name\":\"x\",\"devices\":{}}")]
    [InlineData("{\"version\":1,\"name\":\"\",\"devices\":{}}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"devices\":{\"a\":{\"color\":\"red\",\"brightness\":10,\"effect\":\"Static\",\"speed\":1}}}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"devices\":{\"a\":{\"color\":\"#FFFFFF\",\"brightness\":101,\"effect\":\"Static\",\"speed\":1}}}")]
    [InlineData("{\"version\":1,\"name\":\"x\",\"devices\":{\"a\":{\"color\":\"#FFFFFF\",\"brightness\":1,\"effect\":\"Static\",\"speed\":-5}}}")]
    [InlineData("null")]
    public void InvalidProfilesAreRejected(string json) =>
        Assert.Throws<ProfileValidationException>(() => ProfileValidator.ParseAndValidate(json));

    [Fact] public void OversizedFileRejected()
    {
        Directory.CreateDirectory(_dir);
        var f = Path.Combine(_dir, "big.json");
        File.WriteAllText(f, new string(' ', ProfileValidator.MaxFileBytes + 1));
        Assert.Throws<ProfileValidationException>(() => new ProfileStore(_dir).Import(f));
    }

    [Fact] public void NameCannotEscapeStoreDirectory()
    {
        var store = new ProfileStore(_dir);
        store.Save(Sample("..\\..\\evil/x"));
        Assert.All(Directory.GetFiles(_dir, "*.json"), f => Assert.Equal(Path.GetFullPath(_dir), Path.GetDirectoryName(f)));
    }

    [Fact] public void SettingsClampBadValuesAndRejectRemoteHost()
    {
        var s = new AppSettings { OpenRgbHost = "192.168.1.5", OpenRgbPort = 0, EffectFps = 5000 };
        s.Normalize();
        Assert.Equal("127.0.0.1", s.OpenRgbHost); Assert.Equal(6742, s.OpenRgbPort); Assert.Equal(60, s.EffectFps);
    }
}

public class PlatformTests
{
    [Fact] public void DesktopEntryIsMinimizedAndQuoted()
    {
        var e = StartupManager.DesktopEntry("/opt/Free RGB/FreeRGB");
        Assert.Contains("Exec=\"/opt/Free RGB/FreeRGB\" --minimized", e);
    }

    [Fact] public void WindowsRunKeyRoundTrip()
    {
        if (!OperatingSystem.IsWindows()) return; // registry implementation is Windows-only
        var key = "FreeRGB-Test-" + Guid.NewGuid().ToString("N");
        var exe = @"C:\Program Files\FreeRGB\FreeRGB.exe";
        try
        {
            Assert.False(StartupManager.IsEnabled(exe, key));
            StartupManager.SetEnabled(true, exe, key);
            Assert.True(StartupManager.IsEnabled(exe, key));
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            Assert.Equal("\"" + exe + "\" --minimized", k!.GetValue(key));
            StartupManager.SetEnabled(false, exe, key);
            Assert.False(StartupManager.IsEnabled(exe, key));
        }
        finally { StartupManager.SetEnabled(false, exe, key); }
    }

    [Fact] public async Task SecondInstanceIsRefusedAndSignalsFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "freergb-si-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new SingleInstance(dir);
            Assert.True(first.TryAcquire());
            var shown = new TaskCompletionSource();
            first.ShowRequested += () => shown.TrySetResult();
            using var second = new SingleInstance(dir);
            Assert.False(second.TryAcquire());
            Assert.True(await second.SignalExistingAsync());
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact] public async Task LockReleasedAfterDispose()
    {
        var dir = Path.Combine(Path.GetTempPath(), "freergb-si-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = new SingleInstance(dir); Assert.True(a.TryAcquire()); a.Dispose();
            using var b = new SingleInstance(dir); Assert.True(b.TryAcquire());
            await Task.CompletedTask;
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
