using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using FreeRGB.Core;
using FreeRGB.Core.Backends;

namespace FreeRGB.Tests;

/// <summary>
/// SIMULATION of an OpenRGB SDK server (protocol ≤5) for automated tests. This is NOT hardware and
/// must never be reported as hardware verification.
/// </summary>
public sealed class FakeOpenRgbServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly List<TcpClient> _clients = new();
    private readonly object _lock = new();
    private Task? _accept;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int ServerProtocol { get; set; } = 5;
    public bool AnswerVersion { get; set; } = true;
    public List<DeviceInfo> Devices { get; } = new();
    public List<(uint Pkt, uint Dev, byte[] Payload)> Received { get; } = new();

    public FakeOpenRgbServer Start()
    {
        _listener.Start();
        _accept = Task.Run(AcceptLoop);
        return this;
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var c = await _listener.AcceptTcpClientAsync(_cts.Token);
                lock (_lock) _clients.Add(c);
                _ = Task.Run(() => Serve(c));
            }
        }
        catch { }
    }

    private async Task Serve(TcpClient c)
    {
        var s = c.GetStream();
        var h = new byte[16];
        int negotiated = 0;
        try
        {
            while (true)
            {
                await s.ReadExactlyAsync(h, _cts.Token);
                OpenRgbProtocol.TryParseHeader(h, out var dev, out var pkt, out var size);
                var payload = new byte[size];
                if (size > 0) await s.ReadExactlyAsync(payload, _cts.Token);
                lock (_lock) Received.Add((pkt, dev, payload));

                switch (pkt)
                {
                    case OpenRgbProtocol.RequestProtocolVersion:
                        if (!AnswerVersion) break;
                        var client = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                        negotiated = (int)Math.Min(client, (uint)ServerProtocol);
                        var v = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(v, (uint)ServerProtocol);
                        await s.WriteAsync(OpenRgbProtocol.BuildPacket(0, pkt, v));
                        break;
                    case OpenRgbProtocol.RequestControllerCount:
                        var n = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(n, (uint)Devices.Count);
                        await s.WriteAsync(OpenRgbProtocol.BuildPacket(0, pkt, n));
                        break;
                    case OpenRgbProtocol.RequestControllerData:
                        var body = SerializeDevice(Devices[(int)dev], negotiated);
                        await s.WriteAsync(OpenRgbProtocol.BuildPacket(dev, pkt, body));
                        break;
                }
            }
        }
        catch { }
    }

    public async Task NotifyDeviceListUpdatedAsync()
    {
        TcpClient[] cs; lock (_lock) cs = _clients.ToArray();
        foreach (var c in cs) { try { await c.GetStream().WriteAsync(OpenRgbProtocol.BuildPacket(0, OpenRgbProtocol.DeviceListUpdated, Array.Empty<byte>())); } catch { } }
    }

    public void DropClients()
    {
        lock (_lock) { foreach (var c in _clients) c.Dispose(); _clients.Clear(); }
    }

    public (uint Pkt, uint Dev, byte[] Payload)[] Snapshot() { lock (_lock) return Received.ToArray(); }

    public static byte[] SerializeDevice(DeviceInfo d, int protocol)
    {
        using var body = new MemoryStream();
        OpenRgbProtocol.WriteI32(body, (int)d.Type);
        OpenRgbProtocol.WriteString(body, d.Name);
        if (protocol >= 1) OpenRgbProtocol.WriteString(body, d.Vendor);
        OpenRgbProtocol.WriteString(body, d.Description);
        OpenRgbProtocol.WriteString(body, "1.0");
        OpenRgbProtocol.WriteString(body, d.Serial);
        OpenRgbProtocol.WriteString(body, d.Location);
        OpenRgbProtocol.WriteU16(body, (ushort)d.Modes.Count);
        OpenRgbProtocol.WriteI32(body, d.ActiveMode);
        foreach (var m in d.Modes) OpenRgbProtocol.WriteMode(body, m, protocol);
        OpenRgbProtocol.WriteU16(body, (ushort)d.Zones.Count);
        foreach (var z in d.Zones)
        {
            OpenRgbProtocol.WriteString(body, z.Name);
            OpenRgbProtocol.WriteI32(body, z.Type);
            OpenRgbProtocol.WriteU32(body, z.LedsMin); OpenRgbProtocol.WriteU32(body, z.LedsMax); OpenRgbProtocol.WriteU32(body, z.LedsCount);
            OpenRgbProtocol.WriteU16(body, 0); // matrix map
            if (protocol >= 4) OpenRgbProtocol.WriteU16(body, 0); // segments
            if (protocol >= 5) OpenRgbProtocol.WriteU32(body, 0); // zone flags
        }
        OpenRgbProtocol.WriteU16(body, (ushort)d.LedNames.Count);
        foreach (var l in d.LedNames) { OpenRgbProtocol.WriteString(body, l); OpenRgbProtocol.WriteU32(body, 0); }
        OpenRgbProtocol.WriteU16(body, (ushort)d.Colors.Count);
        foreach (var c in d.Colors) OpenRgbProtocol.WriteColor(body, c);
        using var ms = new MemoryStream();
        OpenRgbProtocol.WriteU32(ms, (uint)(4 + body.Length));
        body.WriteTo(ms);
        return ms.ToArray();
    }

    public static DeviceInfo SampleStripWithStatic(string name = "Test Strip", string serial = "S1", int leds = 4)
    {
        var d = new DeviceInfo
        {
            Name = name, Vendor = "Acme", Description = "sim", Serial = serial, Location = "HID: sim", Type = DeviceType.LedStrip,
            Modes =
            {
                new ModeInfo { Name = "Direct", Flags = ModeFlags.HasPerLedColor, ColorMode = ColorMode.PerLed },
                new ModeInfo { Name = "Static", Flags = ModeFlags.HasModeSpecificColor | ModeFlags.HasBrightness, ColorsMin = 1, ColorsMax = 1,
                               BrightnessMin = 0, BrightnessMax = 100, Brightness = 100, ColorMode = ColorMode.ModeSpecific, Colors = { new Rgb(1, 2, 3) } },
                new ModeInfo { Name = "Rainbow Wave", Flags = ModeFlags.HasSpeed, SpeedMin = 10, SpeedMax = 2, Speed = 5, ColorMode = ColorMode.None },
            },
            Zones = { new ZoneInfo { Name = "Strip", Type = 1, LedsMin = 1, LedsMax = (uint)leds, LedsCount = (uint)leds } },
        };
        for (int i = 0; i < leds; i++) { d.LedNames.Add($"LED {i}"); d.Colors.Add(Rgb.Black); }
        d.Id = DeviceInfo.MakeId(d.Name, d.Serial, d.Location);
        return d;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        DropClients();
        if (_accept is not null) { try { await _accept.WaitAsync(TimeSpan.FromSeconds(1)); } catch { } }
        _cts.Dispose();
    }
}
