using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Models;

namespace TidalUi3.Services;

public class SyncState
{
    public List<Track> Queue { get; set; } = new();
    public int CurrentIndex { get; set; } = -1;
    public double PositionSeconds { get; set; } = 0;
    public double DurationSeconds { get; set; } = 0;
    public double Volume { get; set; } = 0.75;
    public bool IsPlaying { get; set; }
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; }
    public string ActiveDeviceId { get; set; } = string.Empty;
    public string ActiveDeviceName { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;
}

public class SyncCommand
{
    public string Type { get; set; } = string.Empty; // PLAY, PAUSE, NEXT, PREV, SEEK, VOLUME, TRANSFER
    public string? Value { get; set; }
    public string TargetDeviceId { get; set; } = string.Empty;
}

public class DeviceInfo
{
    [System.Text.Json.Serialization.JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    public string Name => DeviceName; // For UI binding
}

/// <summary>
/// WebSocket link to the sync server. Events are raised on background threads. Once
/// connected it stays connected: a dropped connection is retried with a growing delay
/// until it is back or <see cref="Disconnect"/> is called.
/// </summary>
public class SyncService : IDisposable
{
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(2);

    public string DeviceId => _deviceId;
    public string DeviceName => _deviceName;

    public int ConnectedDevicesCount
    {
        get { lock (_activeDevices) return _activeDevices.Count; }
    }

    public IReadOnlyList<DeviceInfo> ActiveDevices
    {
        get { lock (_activeDevices) return _activeDevices.ToArray(); }
    }

    public event Action<SyncState>? StateReceived;
    public event Action<SyncCommand>? CommandReceived;
    public event Action<List<DeviceInfo>>? DevicesUpdated;
    public event Action<bool>? ConnectionStatusChanged;

    // Written by the receive loop's thread, read from the UI: always under its own lock.
    private readonly List<DeviceInfo> _activeDevices = new();
    private ClientWebSocket? _ws;

    // ClientWebSocket allows one send at a time. State updates, commands and pings used to
    // overlap; the loser threw, and a ping that threw ended the ping loop for good.
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    // One connection attempt at a time: the retry loop and the settings page can both try.
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    // Set by ConnectAsync, cleared by Disconnect. While set, a lost connection is retried.
    private volatile bool _wantsConnection;
    private int _isRetrying;

    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly int _userId;
    private string _baseUrl => SettingsService.SyncServerUrl;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public SyncService(int userId)
    {
        _userId = userId;
        _deviceId = GetOrCreateDeviceId();
        _deviceName = Environment.MachineName;
    }

    private string GetOrCreateDeviceId()
    {
        var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
        if (settings.Values["DeviceId"] is string id) return id;
        id = Guid.NewGuid().ToString();
        settings.Values["DeviceId"] = id;
        return id;
    }

    /// <summary>
    /// Connects, and keeps retrying in the background if this attempt fails. The result
    /// reports this attempt only, so the settings page can show why it failed.
    /// </summary>
    public async Task<(bool Success, string? Error)> ConnectAsync()
    {
        if (!SettingsService.SyncEnabled) return (false, "Sync is disabled in settings");

        _wantsConnection = true;
        var result = await TryConnectAsync();
        if (!result.Success) ScheduleRetry();
        return result;
    }

    public async Task ReconnectAsync()
    {
        Disconnect();
        await ConnectAsync();
    }

    public void Disconnect()
    {
        _wantsConnection = false;
        var ws = Interlocked.Exchange(ref _ws, null);
        ws?.Dispose();
        ClearDevices();
        ConnectionStatusChanged?.Invoke(false);
    }

    private async Task<(bool Success, string? Error)> TryConnectAsync()
    {
        await _connectLock.WaitAsync();
        try
        {
            if (IsConnected) return (true, null);

            // A socket left behind by a dropped connection can't be reused.
            var ws = new ClientWebSocket();
            Interlocked.Exchange(ref _ws, ws)?.Dispose();

            var uri = new Uri($"{_baseUrl}?userId={_userId}&deviceId={_deviceId}&deviceName={Uri.EscapeDataString(_deviceName)}");
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await ws.ConnectAsync(uri, cts.Token);
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _ws, null, ws);
                ws.Dispose();
                ConnectionStatusChanged?.Invoke(false);
                return (false, ex.Message);
            }

            // Disconnect may have run while the handshake was in flight.
            if (!_wantsConnection || _ws != ws)
            {
                ws.Dispose();
                return (false, "Disconnected");
            }

            ConnectionStatusChanged?.Invoke(true);
            _ = ReceiveLoop(ws);
            _ = PingLoop(ws);
            return (true, null);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private void ScheduleRetry()
    {
        if (!_wantsConnection || Interlocked.Exchange(ref _isRetrying, 1) == 1) return;
        _ = RetryLoopAsync();
    }

    private async Task RetryLoopAsync()
    {
        try
        {
            var delay = FirstRetryDelay;
            while (_wantsConnection && SettingsService.SyncEnabled && !IsConnected)
            {
                await Task.Delay(delay);
                if (!_wantsConnection || IsConnected) break;
                if ((await TryConnectAsync()).Success) break;
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
        catch { /* the next drop schedules a fresh loop */ }
        finally
        {
            Interlocked.Exchange(ref _isRetrying, 0);
        }

        // A drop right after this loop connected found it still running and was skipped.
        if (_wantsConnection && SettingsService.SyncEnabled && !IsConnected)
            ScheduleRetry();
    }

    /// <summary>Handles the end of <paramref name="ws"/>, unless it was already replaced or closed on purpose.</summary>
    private void OnConnectionLost(ClientWebSocket ws)
    {
        if (Interlocked.CompareExchange(ref _ws, null, ws) != ws) return;

        ws.Dispose();
        ClearDevices();
        ConnectionStatusChanged?.Invoke(false);
        ScheduleRetry();
    }

    private void ClearDevices()
    {
        lock (_activeDevices) _activeDevices.Clear();
        DevicesUpdated?.Invoke(new List<DeviceInfo>());
    }

    private async Task PingLoop(ClientWebSocket ws)
    {
        while (_ws == ws && ws.State == WebSocketState.Open)
        {
            await Task.Delay(30000);
            if (_ws != ws || ws.State != WebSocketState.Open) break;
            try
            {
                await SendAsync(new { type = "PING" });
            }
            catch { break; } // the receive loop notices the drop and reconnects
        }
    }

    private async Task ReceiveLoop(ClientWebSocket ws)
    {
        var buffer = new byte[1024 * 64];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                // Accumulate frames until EndOfMessage
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                try
                {
                    var message = JsonSerializer.Deserialize<JsonElement>(json);
                    HandleMessage(message);
                }
                catch { /* skip malformed message, keep connection alive */ }
            }
        }
        catch { /* dropped, or disposed by Disconnect */ }
        finally
        {
            OnConnectionLost(ws);
        }
    }

    private void HandleMessage(JsonElement message)
    {
        try
        {
            var type = message.GetProperty("type").GetString();
            switch (type)
            {
                case "INIT":
                    var devices = message.GetProperty("devices").Deserialize<List<DeviceInfo>>(TidalApiClient.JsonOptions);
                    if (devices != null)
                    {
                        UpdateDevices(list =>
                        {
                            list.Clear();
                            list.AddRange(devices.FindAll(d => d.DeviceId != _deviceId));
                        });
                    }
                    if (message.TryGetProperty("state", out var stateElement) && stateElement.ValueKind != JsonValueKind.Null)
                    {
                        var state = stateElement.Deserialize<SyncState>(TidalApiClient.JsonOptions);
                        if (state != null && state.ActiveDeviceId != _deviceId) StateReceived?.Invoke(state);
                    }
                    break;

                case "DEVICE_JOINED":
                    var newDev = message.GetProperty("device").Deserialize<DeviceInfo>(TidalApiClient.JsonOptions);
                    if (newDev != null && newDev.DeviceId != _deviceId)
                    {
                        UpdateDevices(list =>
                        {
                            if (!list.Exists(d => d.DeviceId == newDev.DeviceId))
                                list.Add(newDev);
                        });
                    }
                    break;

                case "DEVICE_LEFT":
                    var leftId = message.GetProperty("deviceId").GetString();
                    UpdateDevices(list => list.RemoveAll(d => d.DeviceId == leftId));
                    break;

                case "SYNC_STATE":
                    var data = message.GetProperty("data").Deserialize<SyncState>(TidalApiClient.JsonOptions);
                    if (data != null && data.ActiveDeviceId != _deviceId) StateReceived?.Invoke(data);
                    break;

                case "COMMAND":
                    if (message.TryGetProperty("data", out var cmdData))
                    {
                        var cmd = cmdData.Deserialize<SyncCommand>(TidalApiClient.JsonOptions);
                        if (cmd != null && (string.IsNullOrEmpty(cmd.TargetDeviceId) || cmd.TargetDeviceId == _deviceId))
                            CommandReceived?.Invoke(cmd);
                    }
                    break;
            }
        }
        catch { }
    }

    private void UpdateDevices(Action<List<DeviceInfo>> change)
    {
        List<DeviceInfo> snapshot;
        lock (_activeDevices)
        {
            change(_activeDevices);
            snapshot = new List<DeviceInfo>(_activeDevices);
        }
        DevicesUpdated?.Invoke(snapshot);
    }

    public async Task SendCommandAsync(SyncCommand command)
    {
        if (!IsConnected) return;
        await SendAsync(new { type = "COMMAND", data = command });
    }

    public async Task UpdateStateAsync(SyncState state)
    {
        if (!IsConnected) return;
        state.ActiveDeviceId = _deviceId;
        state.ActiveDeviceName = _deviceName;
        state.LastUpdated = DateTimeOffset.Now;

        await SendAsync(new { type = "UPDATE_STATE", data = state });
    }

    private async Task SendAsync(object payload)
    {
        // A state update carries the whole queue; serialize it off the caller's (UI) thread.
        var bytes = await Task.Run(() =>
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, TidalApiClient.JsonOptions)));

        await _sendLock.WaitAsync();
        try
        {
            if (_ws is not { State: WebSocketState.Open } ws) return;
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Closes the socket and stops reconnecting. Disposing the service is just the
    /// deterministic way to reach Disconnect when the window goes away.
    /// </summary>
    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
