using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DiscordRPC;
using TidalUi3.Models;
using Windows.ApplicationModel.Resources;

namespace TidalUi3.Services;

public sealed class DiscordRpcService : IDisposable
{
    private DiscordRpcClient? _client;
    private bool _isConnected;
    private readonly string _applicationId = "1490026604464832732";
    private readonly ResourceLoader _rl = new();
    
    public bool IsEnabled => SettingsService.DiscordRichPresence;

    public DiscordRpcService()
    {
    }

    public void Connect()
    {
        if (_isConnected || !IsEnabled) return;

        try
        {
            _client = new DiscordRpcClient(_applicationId);
            _client.Initialize();
            _isConnected = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Discord RPC Connection failed: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        if (!_isConnected) return;

        try
        {
            _client?.Dispose();
            _client = null;
            _isConnected = false;
        }
        catch { }
    }

    public void UpdatePresence(Track? track, bool isPlaying, double positionSeconds)
    {
        if (!IsEnabled)
        {
            Disconnect();
            return;
        }
        
        if (!_isConnected)
        {
            Connect();
        }

        if (_client == null || !_isConnected || track == null) return;

        try
        {
            if (isPlaying)
            {
                var duration = TimeSpan.FromSeconds(track.DurationSeconds);
                var endTime = DateTime.UtcNow.AddSeconds(track.DurationSeconds - positionSeconds);

                _client.SetPresence(new RichPresence
                {
                    Type = ActivityType.Listening,
                    Details = track.Title,
                    State = $"{_rl.GetString("Discord_By")}{track.Artist}",
                    Timestamps = new Timestamps
                    {
                        Start = DateTime.UtcNow.AddSeconds(-positionSeconds),
                        End = duration.TotalSeconds > 0 ? endTime : null
                    },
                    Assets = new Assets
                    {
                        LargeImageKey = string.IsNullOrEmpty(track.CoverUrl) ? "tid3_logo" : track.CoverUrl,
                        LargeImageText = track.Album,
                        SmallImageKey = "play_icon",
                        SmallImageText = _rl.GetString("Discord_Listening")
                    }
                });
            }
            else
            {
                ClearPresence();
            }
        }
        catch (Exception ex)
        {
             Debug.WriteLine($"Discord RPC Update failed: {ex.Message}");
        }
    }

    public void ClearPresence()
    {
        if (!_isConnected || _client == null) return;
        
        try
        {
            _client.ClearPresence();
        }
        catch { }
    }

    public void Dispose()
    {
        Disconnect();
    }
}
