// Simulated OpenRGB SDK server for measuring FreeRGB without hardware. NOT hardware.
// usage: SimServer <ignored> [ledsPerStrip=60] [stripCount=1] [portFile] [countFile]
using FreeRGB.Tests;

int port = args.Length > 0 ? int.Parse(args[0]) : 6743;
int leds = args.Length > 1 ? int.Parse(args[1]) : 60;
int count = args.Length > 2 ? int.Parse(args[2]) : 1;

// Fixed port requires own listener; FakeOpenRgbServer binds port 0, so this tool reports the port it got.
await using var srv = new FakeOpenRgbServer().Start();
for (int i = 0; i < count; i++) srv.Devices.Add(FakeOpenRgbServer.SampleStripWithStatic($"Sim Strip {i}", $"SIM{i}", leds));
if (args.Length > 3) File.WriteAllText(args[3], srv.Port.ToString());
else Console.WriteLine($"PORT={srv.Port}");
Console.Out.Flush();
while (true)
{
    await Task.Delay(1000);
    // Count UPDATELEDS packets received so a measurement can prove an effect really streams frames.
    var n = srv.Snapshot().Count(p => p.Pkt == FreeRGB.Core.Backends.OpenRgbProtocol.UpdateLeds);
    if (args.Length > 4) { try { File.WriteAllText(args[4], n.ToString()); } catch (IOException) { } }
    else Console.WriteLine($"RX_UPDATELEDS={n}");
}
