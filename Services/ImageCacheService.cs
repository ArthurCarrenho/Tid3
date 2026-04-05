using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace TidalUi3.Services;

public static class ImageCacheService
{
    private static readonly HttpClient _http = new();
    private static readonly StorageFolder _cacheFolder = ApplicationData.Current.LocalCacheFolder;
    private static readonly object _lock = new();

    public static async Task<BitmapImage?> GetImageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var filename = GetMd5Hash(url) + ".jpg";
            var filePath = Path.Combine(_cacheFolder.Path, filename);

            if (!File.Exists(filePath))
            {
                var bytes = await _http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);
            }

            return new BitmapImage(new Uri(filePath));
        }
        catch
        {
            // Fallback to direct load or null
            return new BitmapImage(new Uri(url));
        }
    }

    public static async Task<long> GetCacheSizeAsync()
    {
        try
        {
            long size = 0;
            var files = await _cacheFolder.GetFilesAsync();
            foreach (var file in files)
            {
                var props = await file.GetBasicPropertiesAsync();
                size += (long)props.Size;
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }

    public static async Task ClearCacheAsync()
    {
        try
        {
            var files = await _cacheFolder.GetFilesAsync();
            foreach (var file in files)
            {
                await file.DeleteAsync();
            }
        }
        catch { }
    }

    private static string GetMd5Hash(string input)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
