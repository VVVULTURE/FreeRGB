namespace FreeRGB.Core.Backends;

public enum BackendState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>Backend service is not running / not reachable.</summary>
    Unavailable,
}

public sealed record BackendStatus(BackendState State, string Message);

/// <summary>
/// Common interface for hardware integrations. A backend discovers devices, reports their real
/// capabilities, and applies lighting changes. Different backends may use entirely different
/// mechanisms (a local service, a documented HID protocol, a vendor SDK).
/// </summary>
public interface IDeviceBackend : IAsyncDisposable
{
    string Name { get; }
    BackendStatus Status { get; }
    IReadOnlyList<DeviceInfo> Devices { get; }

    /// <summary>Raised when the device list or backend status changes.</summary>
    event Action? Changed;

    /// <summary>Connects (or reconnects) and enumerates devices. Never throws for ordinary connectivity failures.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Re-enumerates devices on an existing connection.</summary>
    Task RescanAsync(CancellationToken ct = default);

    /// <summary>Activates a mode on a device. The mode (including speed/colours/brightness) is sent as given.</summary>
    Task SetModeAsync(DeviceInfo device, int modeIndex, ModeInfo mode, CancellationToken ct = default);

    /// <summary>Sets every LED colour of a device (the device must be in a per-LED capable mode).</summary>
    Task SetLedsAsync(DeviceInfo device, IReadOnlyList<Rgb> colors, CancellationToken ct = default);

    /// <summary>Sets how many LEDs a resizable zone (e.g. an addressable header) has. Callers should rescan afterwards.</summary>
    Task ResizeZoneAsync(DeviceInfo device, int zoneIndex, int newSize, CancellationToken ct = default);

    /// <summary>Sets the LEDs of one zone.</summary>
    Task SetZoneLedsAsync(DeviceInfo device, int zoneIndex, IReadOnlyList<Rgb> colors, CancellationToken ct = default);
}
