using System;
using System.Collections.Concurrent;
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
    // Cached covers live in their own subfolder. They used to sit directly in
    // LocalCacheFolder, which SessionStorageService also writes to, so clearing the image
    // cache deleted the saved playback session along with the thumbnails.
    private const string CacheFolderName = "ImageCache";

    private static readonly HttpClient _http = new();
    private static readonly string _cacheDir = InitCacheDir();

    // One download per URL: list items routinely request the same cover simultaneously,
    // and letting each one write the same file races on the filesystem.
    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new();

    private static string InitCacheDir()
    {
        var dir = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, CacheFolderName);
        Directory.CreateDirectory(dir);
        _ = Task.Run(PurgeLegacyCache);
        return dir;
    }

    /// <summary>
    /// Removes cover files left in the cache root by the previous layout. Only .jpg files
    /// were ever written there by this service, so nothing else is touched.
    /// </summary>
    private static void PurgeLegacyCache()
    {
        try
        {
            var root = ApplicationData.Current.LocalCacheFolder.Path;
            foreach (var file in Directory.EnumerateFiles(root, "*.jpg", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(file); } catch { /* in use or already gone */ }
            }
        }
        catch { }
    }

    public static async Task<BitmapImage?> GetImageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var path = await EnsureCachedAsync(url);
            if (path is not null) return new BitmapImage(new Uri(path));
        }
        catch { }

        // Cache unavailable: let the image element stream straight from the network.
        return new BitmapImage(new Uri(url));
    }

    private static Task<string?> EnsureCachedAsync(string url)
    {
        var path = Path.Combine(_cacheDir, GetCacheKey(url) + ".jpg");
        if (File.Exists(path)) return Task.FromResult<string?>(path);

        // Lazy guarantees the factory runs once even when several callers race here;
        // ConcurrentDictionary.GetOrAdd alone may invoke its factory more than once.
        return _inFlight.GetOrAdd(url, key => new Lazy<Task<string?>>(
            () => DownloadAsync(key, path))).Value;
    }

    private static async Task<string?> DownloadAsync(string url, string path)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);

            // Write to a temp file and move it into place so readers only ever observe a
            // complete image. A direct write that is interrupted leaves a truncated file
            // that File.Exists accepts forever, permanently breaking that cover.
            var temp = $"{path}.{Guid.NewGuid():n}.tmp";
            await File.WriteAllBytesAsync(temp, bytes);

            try
            {
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                // Another writer may have completed the same file first; that is fine.
                if (!File.Exists(path)) throw;
            }

            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove(url, out _);
        }
    }

    public static Task<long> GetCacheSizeAsync() => Task.Run(() =>
    {
        try
        {
            long size = 0;
            foreach (var file in Directory.EnumerateFiles(_cacheDir))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            return size;
        }
        catch
        {
            return 0L;
        }
    });

    public static Task ClearCacheAsync() => Task.Run(() =>
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_cacheDir))
            {
                // A cover still bound to a live BitmapImage keeps its handle open; skip
                // those rather than aborting the whole sweep.
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    });

    // MD5 is used purely to derive a stable filename from a URL, never for security.
    private static string GetCacheKey(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
