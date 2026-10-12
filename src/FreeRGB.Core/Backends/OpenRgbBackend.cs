using System.Net.Sockets;

namespace FreeRGB.Core.Backends;

/// <summary>
/// Backend that talks to a running OpenRGB SDK server (default 127.0.0.1:6742). OpenRGB does the
/// actual device protocols; FreeRGB never sends raw hardware commands itself through this backend.
/// </summary>
public sealed class OpenRgbBackend : IDeviceBackend
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly object _stateLock = new();

    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private TaskCompletionSource<byte[]>? _pending;
    private uint _pendingId;
    private int _protocol;
    private List<DeviceInfo> _devices = new();
    private BackendStatus _status = new(BackendState.Disconnected, "Not connected.");
    private bool _disposed;

    public OpenRgbBackend(string host = "127.0.0.1", int port = OpenRgbProtocol.DefaultPort, TimeSpan? timeout = null)
    {
        _host = host;
        _port = port;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    public string Name => "OpenRGB SDK server";
    public BackendStatus Status { get { lock (_stateLock) return _status; } }
    public IReadOnlyList<DeviceInfo> Devices { get { lock (_stateLock) return _devices; } }
    public int NegotiatedProtocol => _protocol;
    public event Action? Changed;

    private void SetStatus(BackendState s, string msg)
    {
        lock (_stateLock) _status = new(s, msg);
        Changed?.Invoke();
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed) return;
        await TearDownAsync();
        SetStatus(BackendState.Connecting, $"Connecting to OpenRGB at {_host}:{_port}…");
        try
        {
            _tcp = new TcpClient { NoDelay = true };
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(_timeout);
                await _tcp.ConnectAsync(_host, _port, cts.Token);
            }
            _stream = _tcp.GetStream();
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_stream, _readCts.Token));

            var name = System.Text.Encoding.UTF8.GetBytes("FreeRGB\0");
            await WriteAsync(0, OpenRgbProtocol.SetClientName, name, ct);

            // Version negotiation. If the server never answers it is a protocol-0 server (per the SDK docs).
            var ours = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(ours, OpenRgbProtocol.MaxSupportedVersion);
            _protocol = 0;
            try
            {
                var reply = await RequestAsync(0, OpenRgbProtocol.RequestProtocolVersion, ours, TimeSpan.FromSeconds(1), ct);
                if (reply.Length >= 4)
                    _protocol = (int)Math.Min(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(reply), OpenRgbProtocol.MaxSupportedVersion);
            }
            catch (TimeoutException) { _protocol = 0; }

            await EnumerateAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await TearDownAsync();
            SetStatus(BackendState.Unavailable, "Timed out connecting to OpenRGB. Is the OpenRGB SDK server running?");
        }
        catch (SocketException ex)
        {
            await TearDownAsync();
            SetStatus(BackendState.Unavailable,
                $"Could not reach OpenRGB at {_host}:{_port} ({ex.SocketErrorCode}). Start OpenRGB with its SDK server enabled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or ObjectDisposedException)
        {
            await TearDownAsync();
            SetStatus(BackendState.Unavailable, $"OpenRGB connection failed: {ex.Message}");
        }
    }

    public async Task RescanAsync(CancellationToken ct = default)
    {
        if (_stream is null) { await ConnectAsync(ct); return; }
        try { await EnumerateAsync(ct); }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or ObjectDisposedException)
        {
            await TearDownAsync();
            SetStatus(BackendState.Disconnected, $"Lost connection to OpenRGB: {ex.Message}");
        }
    }

    private async Task EnumerateAsync(CancellationToken ct)
    {
        var countReply = await RequestAsync(0, OpenRgbProtocol.RequestControllerCount, Array.Empty<byte>(), _timeout, ct);
        if (countReply.Length < 4) throw new InvalidDataException("Controller count reply too short.");
        int count = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(countReply);
        if (count is < 0 or > 1024) throw new InvalidDataException($"Implausible controller count {count}.");

        var list = new List<DeviceInfo>(count);
        var req = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(req, (uint)_protocol);
        for (int i = 0; i < count; i++)
        {
            var data = await RequestAsync((uint)i, OpenRgbProtocol.RequestControllerData, req, _timeout, ct);
            try
            {
                var d = OpenRgbProtocol.ParseDevice(data, _protocol);
                d.Handle = i;
                d.Backend = Name;
                list.Add(d);
            }
            catch (InvalidDataException) { /* one malformed device must not hide the others */ }
        }
        lock (_stateLock) { _devices = list; _status = new(BackendState.Connected, $"Connected to OpenRGB (protocol {_protocol}), {list.Count} device(s)."); }
        Changed?.Invoke();
    }

    public Task SetModeAsync(DeviceInfo device, int modeIndex, ModeInfo mode, CancellationToken ct = default)
    {
        if (modeIndex < 0 || modeIndex >= device.Modes.Count) throw new ArgumentOutOfRangeException(nameof(modeIndex));
        Validate(mode);
        var payload = OpenRgbProtocol.BuildUpdateMode(modeIndex, mode, _protocol);
        device.ActiveMode = modeIndex;
        return WriteAsync((uint)device.Handle, OpenRgbProtocol.UpdateMode, payload, ct);
    }

    public Task SetLedsAsync(DeviceInfo device, IReadOnlyList<Rgb> colors, CancellationToken ct = default)
    {
        var packet = colors is Rgb[] arr
            ? OpenRgbProtocol.BuildUpdateLedsPacket((uint)device.Handle, arr)
            : OpenRgbProtocol.BuildUpdateLedsPacket((uint)device.Handle, colors.ToArray());
        return WritePacketAsync(packet, ct);
    }

    public Task ResizeZoneAsync(DeviceInfo device, int zoneIndex, int newSize, CancellationToken ct = default)
    {
        if (zoneIndex < 0 || zoneIndex >= device.Zones.Count) throw new ArgumentOutOfRangeException(nameof(zoneIndex));
        var z = device.Zones[zoneIndex];
        if (!z.Resizable) throw new ArgumentException($"Zone '{z.Name}' is not resizable.", nameof(zoneIndex));
        if (newSize < z.LedsMin || newSize > z.LedsMax) throw new ArgumentOutOfRangeException(nameof(newSize), $"Zone '{z.Name}' accepts {z.LedsMin}..{z.LedsMax} LEDs.");
        return WriteAsync((uint)device.Handle, OpenRgbProtocol.ResizeZone, OpenRgbProtocol.BuildResizeZone(zoneIndex, newSize), ct);
    }

    public Task SetZoneLedsAsync(DeviceInfo device, int zoneIndex, IReadOnlyList<Rgb> colors, CancellationToken ct = default)
    {
        if (zoneIndex < 0 || zoneIndex >= device.Zones.Count) throw new ArgumentOutOfRangeException(nameof(zoneIndex));
        return WriteAsync((uint)device.Handle, OpenRgbProtocol.UpdateZoneLeds, OpenRgbProtocol.BuildUpdateZoneLeds(zoneIndex, colors), ct);
    }

    /// <summary>Refuses values outside the ranges the device itself advertised.</summary>
    private static void Validate(ModeInfo m)
    {
        if (m.HasSpeed && (m.Speed < Math.Min(m.SpeedMin, m.SpeedMax) || m.Speed > Math.Max(m.SpeedMin, m.SpeedMax)))
            throw new ArgumentOutOfRangeException(nameof(m), $"Speed {m.Speed} outside device range {m.SpeedMin}..{m.SpeedMax}.");
        if (m.HasBrightness && (m.Brightness < m.BrightnessMin || m.Brightness > m.BrightnessMax))
            throw new ArgumentOutOfRangeException(nameof(m), $"Brightness {m.Brightness} outside device range {m.BrightnessMin}..{m.BrightnessMax}.");
        if (m.Colors.Count > 4096) throw new ArgumentOutOfRangeException(nameof(m), "Too many colours.");
    }

    // ---- I/O -----------------------------------------------------------------------------------
    private Task WriteAsync(uint devId, uint pktId, byte[] payload, CancellationToken ct) =>
        WritePacketAsync(OpenRgbProtocol.BuildPacket(devId, pktId, payload), ct);

    private async Task WritePacketAsync(byte[] packet, CancellationToken ct)
    {
        var stream = _stream ?? throw new IOException("Not connected to OpenRGB.");
        await _requestLock.WaitAsync(ct);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            try { await stream.WriteAsync(packet, cts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("Write to OpenRGB timed out."); }
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or ObjectDisposedException)
        {
            _ = Task.Run(() => OnConnectionLost(ex.Message));
            throw;
        }
        finally { _requestLock.Release(); }
    }

    private async Task<byte[]> RequestAsync(uint devId, uint pktId, byte[] payload, TimeSpan timeout, CancellationToken ct)
    {
        var stream = _stream ?? throw new IOException("Not connected to OpenRGB.");
        await _requestLock.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_stateLock) { _pending = tcs; _pendingId = pktId; }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                await stream.WriteAsync(OpenRgbProtocol.BuildPacket(devId, pktId, payload), cts.Token);
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"OpenRGB did not answer request {pktId} within {timeout.TotalSeconds:0.#}s.");
            }
        }
        finally
        {
            lock (_stateLock) { _pending = null; }
            _requestLock.Release();
        }
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[OpenRgbProtocol.HeaderSize];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(header, ct);
                if (!OpenRgbProtocol.TryParseHeader(header, out _, out var pktId, out var size))
                    throw new InvalidDataException("Bad packet header from OpenRGB.");
                var payload = new byte[size];
                if (size > 0) await stream.ReadExactlyAsync(payload, ct);

                if (pktId == OpenRgbProtocol.DeviceListUpdated)
                {
                    _ = Task.Run(async () => { try { await RescanAsync(); } catch { /* reported via status */ } });
                    continue;
                }
                TaskCompletionSource<byte[]>? tcs = null;
                lock (_stateLock) { if (_pending is not null && _pendingId == pktId) tcs = _pending; }
                tcs?.TrySetResult(payload);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or SocketException)
        {
            if (!ct.IsCancellationRequested)
            {
                var reason = ex is EndOfStreamException ? "OpenRGB closed the connection." : ex.Message;
                _ = Task.Run(() => OnConnectionLost(reason)); // off the read task so teardown can await it
            }
        }
    }

    private async Task OnConnectionLost(string reason)
    {
        if (_disposed) return;
        bool wasConnected;
        lock (_stateLock) wasConnected = _status.State is BackendState.Connected or BackendState.Connecting;
        if (!wasConnected) return;
        await TearDownAsync();
        SetStatus(BackendState.Disconnected, $"Lost connection to OpenRGB: {reason}");
    }

    private async Task TearDownAsync()
    {
        var cts = _readCts; var task = _readTask; var tcp = _tcp;
        _readCts = null; _readTask = null; _tcp = null; _stream = null;
        lock (_stateLock) { _pending?.TrySetException(new IOException("Connection closed.")); _devices = new(); }
        try { cts?.Cancel(); } catch { }
        try { tcp?.Dispose(); } catch { }
        if (task is not null)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
        }
        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await TearDownAsync();
    }
}
