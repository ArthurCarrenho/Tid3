using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;
using TidalUi3.Models;

namespace TidalUi3.Services;

public class SessionState
{
    public List<Track> Queue { get; set; } = new();
    public int CurrentIndex { get; set; } = -1;
    public double PositionSeconds { get; set; } = 0;
    public double DurationSeconds { get; set; } = 0;
    public double Volume { get; set; } = 0.75;
    public bool IsPlaying { get; set; }
    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;
}

public static class SessionStorageService
{
    private const string FileName = "session.dat";
    private static readonly StorageFolder _cacheFolder = ApplicationData.Current.LocalCacheFolder;

    public static async Task SaveAsync(SessionState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, TidalApiClient.JsonOptions);
            var buffer = CryptographicBuffer.ConvertStringToBinary(json, BinaryStringEncoding.Utf8);
            
            var provider = new DataProtectionProvider("LOCAL=user");
            var encryptedBuffer = await provider.ProtectAsync(buffer);
            
            var file = await _cacheFolder.CreateFileAsync(FileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteBufferAsync(file, encryptedBuffer);
        }
        catch { }
    }

    public static async Task<SessionState?> LoadAsync()
    {
        try
        {
            var file = await _cacheFolder.GetFileAsync(FileName);
            var encryptedBuffer = await FileIO.ReadBufferAsync(file);
            
            var provider = new DataProtectionProvider();
            var buffer = await provider.UnprotectAsync(encryptedBuffer);
            var json = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, buffer);
            
            return JsonSerializer.Deserialize<SessionState>(json, TidalApiClient.JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
