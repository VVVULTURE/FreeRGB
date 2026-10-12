using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeRGB.Core;

/// <summary>Lighting configuration for one device, keyed by <see cref="DeviceInfo.Id"/> inside a profile.</summary>
public sealed class DeviceSetting
{
    public bool On { get; set; } = true;
    /// <summary>"#RRGGBB".</summary>
    public string Color { get; set; } = "#FFFFFF";
    /// <summary>0..100.</summary>
    public int Brightness { get; set; } = 100;
    /// <summary>"Static", a device-reported mode name, or a software effect name (see <see cref="SoftwareEffects"/>).</summary>
    public string Effect { get; set; } = EffectNames.Static;
    /// <summary>0..100, mapped onto the device's own speed range.</summary>
    public int Speed { get; set; } = 50;
    /// <summary>Optional per-zone colours (zone name → "#RRGGBB"). Only honoured for Static.</summary>
    public Dictionary<string, string> Zones { get; set; } = new();
    /// <summary>Optional LED counts for resizable zones such as addressable headers (zone name -> count).</summary>
    public Dictionary<string, int> ZoneSizes { get; set; } = new();
    /// <summary>Friendly name remembered so a profile can describe devices that are currently absent.</summary>
    public string? DeviceName { get; set; }
}

public sealed class Profile
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "Default";
    public Dictionary<string, DeviceSetting> Devices { get; set; } = new();

    public Profile Clone() => ProfileJson.Deserialize(ProfileJson.Serialize(this))!;
}

public static class EffectNames
{
    public const string Static = "Static";
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class FreeRgbJsonContext : JsonSerializerContext;

public static class ProfileJson
{
    // Source-generated serializer: no reflection, so the app can be published trimmed.
    public static string Serialize(Profile p) => JsonSerializer.Serialize(p, FreeRgbJsonContext.Default.Profile);
    public static Profile? Deserialize(string json) => JsonSerializer.Deserialize(json, FreeRgbJsonContext.Default.Profile);
}

public sealed class ProfileValidationException(string message) : Exception(message);

public static class ProfileValidator
{
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxDevices = 256;
    public const int MaxNameLength = 64;

    /// <summary>Throws <see cref="ProfileValidationException"/> for anything unsafe or malformed; otherwise returns the profile normalised.</summary>
    public static Profile Validate(Profile? p)
    {
        if (p is null) throw new ProfileValidationException("Profile is empty.");
        if (p.Version != Profile.CurrentVersion)
            throw new ProfileValidationException($"Unsupported profile version {p.Version} (this build reads version {Profile.CurrentVersion}).");
        p.Name = (p.Name ?? "").Trim();
        if (p.Name.Length is 0 or > MaxNameLength) throw new ProfileValidationException($"Profile name must be 1–{MaxNameLength} characters.");
        if (p.Name.Any(char.IsControl)) throw new ProfileValidationException("Profile name contains control characters.");
        p.Devices ??= new();
        if (p.Devices.Count > MaxDevices) throw new ProfileValidationException("Too many devices in profile.");

        foreach (var (key, s) in p.Devices)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 512) throw new ProfileValidationException("Invalid device key.");
            if (s is null) throw new ProfileValidationException($"Device '{key}' has no settings.");
            if (!Rgb.TryParseHex(s.Color, out _)) throw new ProfileValidationException($"Device '{key}': invalid colour '{s.Color}'.");
            if (s.Brightness is < 0 or > 100) throw new ProfileValidationException($"Device '{key}': brightness must be 0–100.");
            if (s.Speed is < 0 or > 100) throw new ProfileValidationException($"Device '{key}': speed must be 0–100.");
            if (string.IsNullOrWhiteSpace(s.Effect) || s.Effect.Length > 128) throw new ProfileValidationException($"Device '{key}': invalid effect name.");
            s.Zones ??= new();
            if (s.Zones.Count > 256) throw new ProfileValidationException($"Device '{key}': too many zones.");
            s.ZoneSizes ??= new();
            if (s.ZoneSizes.Count > 256) throw new ProfileValidationException($"Device '{key}': too many zone sizes.");
            foreach (var (zn, zs) in s.ZoneSizes)
                if (zn.Length > 128 || zs is < 0 or > 4096) throw new ProfileValidationException($"Device '{key}': zone size for '{zn}' must be 0-4096.");
            foreach (var (zn, zc) in s.Zones)
                if (zn.Length > 128 || !Rgb.TryParseHex(zc, out _)) throw new ProfileValidationException($"Device '{key}': invalid zone colour for '{zn}'.");
        }
        return p;
    }

    public static Profile ParseAndValidate(string json)
    {
        if (json.Length > MaxFileBytes) throw new ProfileValidationException("Profile file is too large.");
        try { return Validate(ProfileJson.Deserialize(json)); }
        catch (JsonException ex) { throw new ProfileValidationException("Not a valid FreeRGB profile: " + ex.Message); }
    }
}

/// <summary>Stores one JSON file per profile. Writes are atomic (temp file + move).</summary>
public sealed class ProfileStore
{
    private readonly string _dir;
    public ProfileStore(string directory) { _dir = directory; Directory.CreateDirectory(_dir); }

    public string Directory_ => _dir;

    public static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (cleaned.Length == 0) cleaned = "profile";
        return cleaned + ".json";
    }

    private string PathFor(string name) => Path.Combine(_dir, FileNameFor(name));

    public IReadOnlyList<string> List()
    {
        var names = new List<string>();
        foreach (var f in System.IO.Directory.EnumerateFiles(_dir, "*.json"))
        {
            try { names.Add(Load(f, byPath: true).Name); } catch { /* skip unreadable/invalid files */ }
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public bool Exists(string name) => File.Exists(PathFor(name));

    public Profile Load(string nameOrPath, bool byPath = false)
    {
        var path = byPath ? nameOrPath : PathFor(nameOrPath);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException($"Profile '{nameOrPath}' not found.");
        if (info.Length > ProfileValidator.MaxFileBytes) throw new ProfileValidationException("Profile file is too large.");
        return ProfileValidator.ParseAndValidate(File.ReadAllText(path));
    }

    public void Save(Profile p)
    {
        ProfileValidator.Validate(p);
        AtomicFile.WriteAllText(PathFor(p.Name), ProfileJson.Serialize(p));
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Validates then copies an external file into the store; returns the stored profile.</summary>
    public Profile Import(string path, bool overwrite = false)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("File not found.", path);
        if (info.Length > ProfileValidator.MaxFileBytes) throw new ProfileValidationException("Profile file is too large.");
        var p = ProfileValidator.ParseAndValidate(File.ReadAllText(path));
        if (!overwrite && Exists(p.Name)) throw new ProfileValidationException($"A profile named '{p.Name}' already exists.");
        Save(p);
        return p;
    }

    public void Export(string name, string destination) => AtomicFile.WriteAllText(destination, ProfileJson.Serialize(Load(name)));
}

public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, Path.GetRandomFileName() + ".tmp");
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
