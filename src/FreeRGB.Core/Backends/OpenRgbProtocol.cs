using System.Buffers.Binary;
using System.Text;

namespace FreeRGB.Core.Backends;

/// <summary>
/// Wire-format helpers for the OpenRGB SDK protocol (documented in OpenRGB's Documentation/OpenRGBSDK.md).
/// FreeRGB implements protocol versions 0..5; a server speaking a newer version negotiates down.
/// </summary>
public static class OpenRgbProtocol
{
    public const int MaxSupportedVersion = 5;
    public const int DefaultPort = 6742;
    public const int HeaderSize = 16;
    /// <summary>Upper bound on a single packet we are willing to buffer; guards against a hostile/buggy peer.</summary>
    public const int MaxPacketSize = 16 * 1024 * 1024;

    public const uint RequestControllerCount = 0;
    public const uint RequestControllerData = 1;
    public const uint RequestProtocolVersion = 40;
    public const uint SetClientName = 50;
    public const uint DeviceListUpdated = 100;
    public const uint UpdateLeds = 1050;
    public const uint UpdateZoneLeds = 1051;
    public const uint UpdateSingleLed = 1052;
    public const uint SetCustomMode = 1100;
    public const uint UpdateMode = 1101;

    public static byte[] BuildPacket(uint devId, uint pktId, ReadOnlySpan<byte> payload)
    {
        var buf = new byte[HeaderSize + payload.Length];
        "ORGB"u8.CopyTo(buf);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), devId);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), pktId);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), (uint)payload.Length);
        payload.CopyTo(buf.AsSpan(HeaderSize));
        return buf;
    }

    public static bool TryParseHeader(ReadOnlySpan<byte> h, out uint devId, out uint pktId, out uint size)
    {
        devId = pktId = size = 0;
        if (h.Length < HeaderSize || !h[..4].SequenceEqual("ORGB"u8)) return false;
        devId = BinaryPrimitives.ReadUInt32LittleEndian(h[4..]);
        pktId = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]);
        size = BinaryPrimitives.ReadUInt32LittleEndian(h[12..]);
        return size <= MaxPacketSize;
    }

    // ---- colours -------------------------------------------------------------------------------
    // RGBColor is a little-endian uint 0x00BBGGRR, i.e. bytes R, G, B, 0.
    public static void WriteColor(Stream s, Rgb c) { s.WriteByte(c.R); s.WriteByte(c.G); s.WriteByte(c.B); s.WriteByte(0); }

    public static byte[] BuildUpdateLeds(IReadOnlyList<Rgb> colors)
    {
        using var ms = new MemoryStream();
        var size = 4 + 2 + colors.Count * 4;
        WriteU32(ms, (uint)size);
        WriteU16(ms, checked((ushort)colors.Count));
        foreach (var c in colors) WriteColor(ms, c);
        return ms.ToArray();
    }

    /// <summary>Complete UPDATELEDS packet (header + payload) in a single allocation: used on the animation hot path.</summary>
    public static byte[] BuildUpdateLedsPacket(uint devId, ReadOnlySpan<Rgb> colors)
    {
        int payload = 4 + 2 + colors.Length * 4;
        var buf = new byte[HeaderSize + payload];
        "ORGB"u8.CopyTo(buf);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), devId);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(8), UpdateLeds);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(12), (uint)payload);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(HeaderSize), (uint)payload);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(HeaderSize + 4), checked((ushort)colors.Length));
        int o = HeaderSize + 6;
        foreach (var c in colors) { buf[o++] = c.R; buf[o++] = c.G; buf[o++] = c.B; buf[o++] = 0; }
        return buf;
    }

    public static byte[] BuildUpdateZoneLeds(int zoneIndex, IReadOnlyList<Rgb> colors)
    {
        using var ms = new MemoryStream();
        var size = 4 + 4 + 2 + colors.Count * 4;
        WriteU32(ms, (uint)size);
        WriteU32(ms, (uint)zoneIndex);
        WriteU16(ms, checked((ushort)colors.Count));
        foreach (var c in colors) WriteColor(ms, c);
        return ms.ToArray();
    }

    public static byte[] BuildUpdateMode(int modeIndex, ModeInfo mode, int protocol)
    {
        using var body = new MemoryStream();
        WriteI32(body, modeIndex);
        WriteMode(body, mode, protocol);
        using var ms = new MemoryStream();
        WriteU32(ms, (uint)(4 + body.Length));
        body.WriteTo(ms);
        return ms.ToArray();
    }

    public static void WriteMode(Stream s, ModeInfo m, int protocol)
    {
        WriteString(s, m.Name);
        if (protocol < 6) WriteI32(s, m.Value);
        WriteU32(s, (uint)m.Flags);
        WriteU32(s, m.SpeedMin);
        WriteU32(s, m.SpeedMax);
        if (protocol >= 3) { WriteU32(s, m.BrightnessMin); WriteU32(s, m.BrightnessMax); }
        WriteU32(s, m.ColorsMin);
        WriteU32(s, m.ColorsMax);
        WriteU32(s, m.Speed);
        if (protocol >= 3) WriteU32(s, m.Brightness);
        WriteU32(s, m.Direction);
        WriteU32(s, (uint)m.ColorMode);
        WriteU16(s, checked((ushort)m.Colors.Count));
        foreach (var c in m.Colors) WriteColor(s, c);
    }

    // ---- device data ---------------------------------------------------------------------------
    /// <summary>Parses a REQUEST_CONTROLLER_DATA response payload (protocol 0..5).</summary>
    public static DeviceInfo ParseDevice(ReadOnlySpan<byte> payload, int protocol)
    {
        var r = new Reader(payload);
        r.U32(); // data_size (includes itself)
        var d = new DeviceInfo { Type = (DeviceType)Math.Clamp(r.I32(), 0, (int)DeviceType.Unknown) };
        d.Name = r.Str();
        if (protocol >= 1) d.Vendor = r.Str();
        d.Description = r.Str();
        r.Str(); // version
        d.Serial = r.Str();
        d.Location = r.Str();

        int numModes = r.U16();
        d.ActiveMode = r.I32();
        for (int i = 0; i < numModes; i++) d.Modes.Add(ReadMode(ref r, protocol, i));

        int numZones = r.U16();
        int firstLed = 0;
        for (int i = 0; i < numZones; i++)
        {
            var z = new ZoneInfo { Name = r.Str(), Type = r.I32(), LedsMin = r.U32(), LedsMax = r.U32(), LedsCount = r.U32() };
            int matrixLen = r.U16();
            // matrix map: `matrixLen` bytes (height, width, then height*width values); not needed here
            if (matrixLen > 0) r.Skip(matrixLen);
            if (protocol >= 4)
            {
                int numSegments = r.U16();
                for (int s = 0; s < numSegments; s++)
                {
                    r.Str(); r.I32(); r.U32(); r.U32(); // name, type, start_idx, leds_count
                }
            }
            if (protocol >= 5) r.U32(); // zone_flags
            z.FirstLed = firstLed;
            firstLed += (int)z.LedsCount;
            d.Zones.Add(z);
        }

        int numLeds = r.U16();
        for (int i = 0; i < numLeds; i++)
        {
            d.LedNames.Add(r.Str());
            r.U32(); // led value
        }

        int numColors = r.U16();
        for (int i = 0; i < numColors; i++) d.Colors.Add(r.Color());

        d.Id = DeviceInfo.MakeId(d.Name, d.Serial, d.Location);
        return d;
    }

    private static ModeInfo ReadMode(ref Reader r, int protocol, int index)
    {
        var m = new ModeInfo { Name = r.Str() };
        m.Value = protocol < 6 ? r.I32() : index;
        m.Flags = (ModeFlags)r.U32();
        m.SpeedMin = r.U32();
        m.SpeedMax = r.U32();
        if (protocol >= 3) { m.BrightnessMin = r.U32(); m.BrightnessMax = r.U32(); }
        m.ColorsMin = r.U32();
        m.ColorsMax = r.U32();
        m.Speed = r.U32();
        if (protocol >= 3) m.Brightness = r.U32();
        m.Direction = r.U32();
        m.ColorMode = (ColorMode)r.U32();
        int n = r.U16();
        for (int i = 0; i < n; i++) m.Colors.Add(r.Color());
        return m;
    }

    // ---- primitives ----------------------------------------------------------------------------
    public static void WriteU16(Stream s, ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); s.Write(b); }
    public static void WriteU32(Stream s, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); s.Write(b); }
    public static void WriteI32(Stream s, int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); s.Write(b); }

    /// <summary>Strings are a ushort length (including the trailing NUL) followed by the bytes.</summary>
    public static void WriteString(Stream s, string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        WriteU16(s, checked((ushort)(bytes.Length + 1)));
        s.Write(bytes);
        s.WriteByte(0);
    }

    /// <summary>Bounds-checked little-endian reader; throws <see cref="InvalidDataException"/> on truncated input.</summary>
    public ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _d;
        private int _p;
        public Reader(ReadOnlySpan<byte> d) { _d = d; _p = 0; }

        private ReadOnlySpan<byte> Take(int n)
        {
            if (n < 0 || _p + n > _d.Length) throw new InvalidDataException("Truncated OpenRGB packet.");
            var s = _d.Slice(_p, n); _p += n; return s;
        }
        public void Skip(int n) => Take(n);
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public Rgb Color() { var b = Take(4); return new(b[0], b[1], b[2]); }
        public string Str()
        {
            int len = U16();
            if (len == 0) return "";
            var b = Take(len);
            if (b[^1] == 0) b = b[..^1];
            return Encoding.UTF8.GetString(b);
        }
    }
}
