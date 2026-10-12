namespace FreeRGB.Core;

/// <summary>An 8-bit-per-channel colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public Rgb Scale(double factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return new((byte)Math.Round(R * factor), (byte)Math.Round(G * factor), (byte)Math.Round(B * factor));
    }

    /// <summary>Parses "#RRGGBB" or "RRGGBB". Returns false for anything else.</summary>
    public static bool TryParseHex(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6) return false;
        if (!byte.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
        color = new(r, g, b);
        return true;
    }

    /// <summary>HSV (h in degrees, s and v in 0..1) to RGB.</summary>
    public static Rgb FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

public enum DeviceType
{
    Motherboard = 0, Dram = 1, Gpu = 2, Cooler = 3, LedStrip = 4, Keyboard = 5, Mouse = 6, Mousemat = 7,
    Headset = 8, HeadsetStand = 9, Gamepad = 10, Light = 11, Speaker = 12, Virtual = 13, Storage = 14,
    Case = 15, Microphone = 16, Accessory = 17, Keypad = 18, Laptop = 19, Monitor = 20, Unknown = 21,
}

[Flags]
public enum ModeFlags : uint
{
    None = 0,
    HasSpeed = 1 << 0,
    HasDirectionLR = 1 << 1,
    HasDirectionUD = 1 << 2,
    HasDirectionHV = 1 << 3,
    HasBrightness = 1 << 4,
    HasPerLedColor = 1 << 5,
    HasModeSpecificColor = 1 << 6,
    HasRandomColor = 1 << 7,
    ManualSave = 1 << 8,
    AutomaticSave = 1 << 9,
    AlwaysEntireDevice = 1 << 10,
}

public enum ColorMode : uint { None = 0, PerLed = 1, ModeSpecific = 2, Random = 3 }

/// <summary>A device mode as reported by the device itself (a hardware effect, or "Direct").</summary>
public sealed class ModeInfo
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
    public ModeFlags Flags { get; set; }
    public uint SpeedMin { get; set; }
    public uint SpeedMax { get; set; }
    public uint BrightnessMin { get; set; }
    public uint BrightnessMax { get; set; }
    public uint ColorsMin { get; set; }
    public uint ColorsMax { get; set; }
    public uint Speed { get; set; }
    public uint Brightness { get; set; }
    public uint Direction { get; set; }
    public ColorMode ColorMode { get; set; }
    public List<Rgb> Colors { get; set; } = new();

    public bool HasSpeed => Flags.HasFlag(ModeFlags.HasSpeed) && SpeedMax != SpeedMin; // some devices report min > max (lower number = faster)
    public bool HasBrightness => Flags.HasFlag(ModeFlags.HasBrightness) && BrightnessMax != BrightnessMin;
    public bool HasModeColor => Flags.HasFlag(ModeFlags.HasModeSpecificColor) && ColorsMax > 0;
    public bool HasPerLed => Flags.HasFlag(ModeFlags.HasPerLedColor);

    public ModeInfo Clone()
    {
        var m = (ModeInfo)MemberwiseClone();
        m.Colors = new List<Rgb>(Colors);
        return m;
    }
}

public sealed class ZoneInfo
{
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public uint LedsMin { get; set; }
    public uint LedsMax { get; set; }
    public uint LedsCount { get; set; }
    /// <summary>Index of this zone's first LED within the device's flat LED list.</summary>
    public int FirstLed { get; set; }

    /// <summary>True for zones whose LED count the user must set (e.g. addressable headers).</summary>
    public bool Resizable => LedsMin != LedsMax;
}

/// <summary>Why a device is (or isn't) controllable. Distinguishes the cases the spec asks for.</summary>
public enum Controllability
{
    /// <summary>Can be controlled now.</summary>
    Controllable,
    /// <summary>Reported by the backend, but exposes no LEDs or no usable mode.</summary>
    DetectedNotControllable,
    /// <summary>Previously seen / saved in a profile, but not currently present.</summary>
    Absent,
}

public sealed class DeviceInfo
{
    /// <summary>Stable key across sessions: built from name, serial and location (index is not stable).</summary>
    public string Id { get; set; } = "";
    /// <summary>Backend-specific handle (the OpenRGB controller index for this session).</summary>
    public int Handle { get; set; }
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Description { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Location { get; set; } = "";
    public DeviceType Type { get; set; }
    public int ActiveMode { get; set; }
    public List<ModeInfo> Modes { get; set; } = new();
    public List<ZoneInfo> Zones { get; set; } = new();
    public List<string> LedNames { get; set; } = new();
    public List<Rgb> Colors { get; set; } = new();
    public string Backend { get; set; } = "";

    public int LedCount => LedNames.Count;

    /// <summary>The mode that accepts per-LED colours directly ("Direct"/"Custom"), if the device has one.</summary>
    public ModeInfo? DirectMode =>
        Modes.FirstOrDefault(m => m.Name.Equals("Direct", StringComparison.OrdinalIgnoreCase)) ??
        Modes.FirstOrDefault(m => m.Name.Equals("Custom", StringComparison.OrdinalIgnoreCase)) ??
        Modes.FirstOrDefault(m => m.HasPerLed && m.ColorMode == ColorMode.PerLed);

    public Controllability Controllability =>
        (LedCount == 0 && !Zones.Any(z => z.Resizable)) || Modes.Count == 0 ? Controllability.DetectedNotControllable : Controllability.Controllable;

    public string? WhyNotControllable =>
        (LedCount == 0 && !Zones.Any(z => z.Resizable)) ? "The backend reports this device but it exposes no LEDs." :
        Modes.Count == 0 ? "The backend reports this device but it exposes no lighting modes." : null;

    public static string MakeId(string name, string serial, string location) =>
        string.Join("|", name.Trim(), serial.Trim(), location.Trim());
}
