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

public class SyncService
{
    public string DeviceId => _deviceId;
    public string DeviceName => _deviceName;
    public int ConnectedDevicesCount => _activeDevices.Count;
    public IReadOnlyList<DeviceInfo> ActiveDevices => _activeDevices.AsReadOnly();
    public event Action<SyncState>? StateReceived;
    public event Action<SyncCommand>? CommandReceived;
    public event Action<List<DeviceInfo>>? DevicesUpdated;
    public event Action<bool>? ConnectionStatusChanged;

    private readonly List<DeviceInfo> _activeDevices = new();
    private ClientWebSocket? _ws;
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

    public async Task<(bool Success, string? Error)> ConnectAsync()
    {
        if (!SettingsService.SyncEnabled) return (false, "Sync is disabled in settings");
        if (_ws?.State == WebSocketState.Open) return (true, null);

        _ws = new ClientWebSocket();
        var uri = new Uri($"{_baseUrl}?userId={_userId}&deviceId={_deviceId}&deviceName={Uri.EscapeDataString(_deviceName)}");
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _ws.ConnectAsync(uri, cts.Token);
            ConnectionStatusChanged?.Invoke(true);
            _ = ReceiveLoop();
            _ = PingLoop();
            return (true, null);
        }
        catch (Exception ex)
        {
            _ws?.Dispose();
            _ws = null;
            ConnectionStatusChanged?.Invoke(false);
            return (false, ex.Message);
        }
    }

    public async Task ReconnectAsync()
    {
        Disconnect();
        await ConnectAsync();
    }

    public void Disconnect()
    {
        _ws?.Dispose();
        _ws = null;
        _activeDevices.Clear();
        DevicesUpdated?.Invoke(_activeDevices);
        ConnectionStatusChanged?.Invoke(false);
    }

    private async Task PingLoop()
    {
        while (IsConnected)
        {
            await Task.Delay(30000);
            if (!IsConnected) break;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "PING" }));
                await _ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { break; }
        }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[1024 * 64];

        while (IsConnected)
        {
            try
            {
                // Accumulate frames until EndOfMessage
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        ConnectionStatusChanged?.Invoke(false);
                        return;
                    }
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
            catch (WebSocketException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }

        ConnectionStatusChanged?.Invoke(false);
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
                        _activeDevices.Clear();
                        _activeDevices.AddRange(devices.FindAll(d => d.DeviceId != _deviceId));
                        DevicesUpdated?.Invoke(new List<DeviceInfo>(_activeDevices));
                    }
                    if (message.TryGetProperty("state", out var stateElement) && stateElement.ValueKind != JsonValueKind.Null)
                    {
                        var state = stateElement.Deserialize<SyncState>(TidalApiClient.JsonOptions);
                        if (state != null && state.ActiveDeviceId != _deviceId) StateReceived?.Invoke(state);
                    }
                    break;

                case "DEVICE_JOINED":
                    var newDev = message.GetProperty("device").Deserialize<DeviceInfo>(TidalApiClient.JsonOptions);
                    if (newDev != null && newDev.DeviceId != _deviceId && !_activeDevices.Exists(d => d.DeviceId == newDev.DeviceId))
                    {
                        _activeDevices.Add(newDev);
                        DevicesUpdated?.Invoke(new List<DeviceInfo>(_activeDevices));
                    }
                    break;

                case "DEVICE_LEFT":
                    var leftId = message.GetProperty("deviceId").GetString();
                    _activeDevices.RemoveAll(d => d.DeviceId == leftId);
                    DevicesUpdated?.Invoke(new List<DeviceInfo>(_activeDevices));
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

    public async Task SendCommandAsync(SyncCommand command)
    {
        if (!IsConnected) return;
        var payload = new { type = "COMMAND", data = command };
        var json = JsonSerializer.Serialize(payload, TidalApiClient.JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task UpdateStateAsync(SyncState state)
    {
        if (!IsConnected) return;
        state.ActiveDeviceId = _deviceId;
        state.ActiveDeviceName = _deviceName;
        state.LastUpdated = DateTimeOffset.Now;
        
        var payload = new { type = "UPDATE_STATE", data = state };
        var json = JsonSerializer.Serialize(payload, TidalApiClient.JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
