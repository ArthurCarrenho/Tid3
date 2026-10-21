using System;
using System.IO;
using System.Text.Json;
using TidalUi3.Helpers;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;

namespace TidalUi3.Services;

/// <summary>
/// Persists the TIDAL sign-in. The tokens are encrypted for the current Windows user, like the
/// saved playback session, in LocalState\tidal_session.dat. Earlier versions kept them in plain
/// text in LocalSettings; <see cref="Load"/> moves those over once and deletes them.
/// </summary>
/// <remarks>
/// The API is synchronous because the session is restored before the window exists. The
/// encryption is a WinRT async operation, so it runs on the thread pool and is waited on
/// there: started from the UI thread (an STA), its completion could need that very thread.
/// </remarks>
public static class TokenStorageService
{
    private const string FileName = "tidal_session.dat";

    // Plain-text keys of the old format, read only to migrate.
    private const string AccessTokenKey = "tidal_access_token";
    private const string RefreshTokenKey = "tidal_refresh_token";
    private const string UserIdKey = "tidal_user_id";
    private const string CountryCodeKey = "tidal_country_code";

    // Token refreshes can save from a background thread while the UI saves or clears.
    private static readonly object _lock = new();

    private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;
    private static string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);

    public static void Save(string accessToken, string? refreshToken, int userId, string countryCode)
    {
        var session = new StoredSession(accessToken, refreshToken, userId, countryCode);
        lock (_lock)
        {
            try
            {
                Write(session);
                RemoveLegacy();
            }
            catch (Exception ex)
            {
                // Not falling back to plain text: worst case the next launch asks to sign in.
                AppLog.Exception("TokenSaveError", ex);
            }
        }
    }

    public static StoredSession? Load()
    {
        lock (_lock)
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    return Read();
                }
                catch (Exception ex)
                {
                    // Unreadable (corrupted, or encrypted for another user): sign in again.
                    AppLog.Exception("TokenLoadError", ex);
                    return null;
                }
            }

            var legacy = LoadLegacy();
            if (legacy is not null)
            {
                try
                {
                    Write(legacy);
                    RemoveLegacy();
                }
                catch (Exception ex)
                {
                    // Keep the old entries so the next launch can try again.
                    AppLog.Exception("TokenMigrationError", ex);
                }
            }
            return legacy;
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            try { File.Delete(FilePath); } catch { /* nothing saved */ }
            RemoveLegacy();
        }
    }

    private static void Write(StoredSession session)
    {
        var json = JsonSerializer.Serialize(session);
        var buffer = CryptographicBuffer.ConvertStringToBinary(json, BinaryStringEncoding.Utf8);
        var encrypted = System.Threading.Tasks.Task.Run(() =>
            new DataProtectionProvider("LOCAL=user").ProtectAsync(buffer).AsTask()).GetAwaiter().GetResult();
        CryptographicBuffer.CopyToByteArray(encrypted, out var bytes);

        // Write aside and swap in, so a crash mid-write can't leave a truncated file that
        // signs the user out.
        var temp = FilePath + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, FilePath, overwrite: true);
    }

    private static StoredSession? Read()
    {
        var bytes = File.ReadAllBytes(FilePath);
        var decrypted = System.Threading.Tasks.Task.Run(() =>
            new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.CreateFromByteArray(bytes)).AsTask())
            .GetAwaiter().GetResult();
        var json = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, decrypted);

        var session = JsonSerializer.Deserialize<StoredSession>(json);
        return string.IsNullOrEmpty(session?.AccessToken) ? null : session;
    }

    private static StoredSession? LoadLegacy()
    {
        if (Settings.Values.TryGetValue(AccessTokenKey, out var accessObj)
            && accessObj is string accessToken
            && !string.IsNullOrEmpty(accessToken))
        {
            var refreshToken = Settings.Values.TryGetValue(RefreshTokenKey, out var refreshObj)
                ? refreshObj as string : null;
            var userId = Settings.Values.TryGetValue(UserIdKey, out var userObj)
                ? (userObj is int id ? id : 0) : 0;
            var countryCode = Settings.Values.TryGetValue(CountryCodeKey, out var countryObj)
                ? countryObj as string ?? "US" : "US";

            return new StoredSession(accessToken, refreshToken, userId, countryCode);
        }

        return null;
    }

    private static void RemoveLegacy()
    {
        Settings.Values.Remove(AccessTokenKey);
        Settings.Values.Remove(RefreshTokenKey);
        Settings.Values.Remove(UserIdKey);
        Settings.Values.Remove(CountryCodeKey);
    }
}

public record StoredSession(string AccessToken, string? RefreshToken, int UserId, string CountryCode);
