using System.Text.Json;
using Windows.Storage;

namespace TidalUi3.Services;

public static class TokenStorageService
{
    private const string AccessTokenKey = "tidal_access_token";
    private const string RefreshTokenKey = "tidal_refresh_token";
    private const string UserIdKey = "tidal_user_id";
    private const string CountryCodeKey = "tidal_country_code";

    private static ApplicationDataContainer Settings =>
        ApplicationData.Current.LocalSettings;

    public static void Save(string accessToken, string? refreshToken, int userId, string countryCode)
    {
        Settings.Values[AccessTokenKey] = accessToken;
        Settings.Values[RefreshTokenKey] = refreshToken ?? "";
        Settings.Values[UserIdKey] = userId;
        Settings.Values[CountryCodeKey] = countryCode;
    }

    public static StoredSession? Load()
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

    public static void Clear()
    {
        Settings.Values.Remove(AccessTokenKey);
        Settings.Values.Remove(RefreshTokenKey);
        Settings.Values.Remove(UserIdKey);
        Settings.Values.Remove(CountryCodeKey);
    }
}

public record StoredSession(string AccessToken, string? RefreshToken, int UserId, string CountryCode);
