using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TidalUi3.Services;

public class LastFmService
{
    private static string ApiKey => SettingsService.LastFmApiKey;
    private static string ApiSecret => SettingsService.LastFmApiSecret;

    private const string BaseUrl = "https://ws.audioscrobbler.com/2.0/";
    private static readonly HttpClient _http = new();

    public static string? SessionKey
    {
        get => SettingsService.LastFmSessionKey;
        set => SettingsService.LastFmSessionKey = value;
    }

    public static bool IsConnected => !string.IsNullOrEmpty(SessionKey);

    private static string GenerateSignature(Dictionary<string, string> parameters)
    {
        var sortedParams = parameters
            .Where(p => p.Key != "format" && p.Key != "callback")
            .OrderBy(p => p.Key)
            .Select(p => p.Key + p.Value);

        var signatureString = string.Join("", sortedParams) + ApiSecret;

        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(signatureString);
        var hash = md5.ComputeHash(bytes);
        return string.Join("", hash.Select(b => b.ToString("x2")));
    }

    private static async Task<JsonElement?> ExecuteRequestAsync(Dictionary<string, string> parameters, HttpMethod method)
    {
        if (string.IsNullOrEmpty(ApiKey)) return null;

        parameters["api_key"] = ApiKey;
        parameters["api_sig"] = GenerateSignature(parameters);
        parameters["format"] = "json";

        try
        {
            HttpResponseMessage response;
            if (method == HttpMethod.Post)
            {
                var content = new FormUrlEncodedContent(parameters);
                response = await _http.PostAsync(BaseUrl, content);
            }
            else
            {
                var query = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
                response = await _http.GetAsync($"{BaseUrl}?{query}");
            }

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> GetAuthTokenAsync()
    {
        var parameters = new Dictionary<string, string> { { "method", "auth.getToken" } };
        var result = await ExecuteRequestAsync(parameters, HttpMethod.Get);
        return result?.GetProperty("token").GetString();
    }

    public static async Task<bool> AuthorizeSessionAsync(string token)
    {
        var parameters = new Dictionary<string, string>
        {
            { "method", "auth.getSession" },
            { "token", token }
        };

        var result = await ExecuteRequestAsync(parameters, HttpMethod.Get);

        if (result.HasValue && result.Value.TryGetProperty("session", out var sessionElement))
        {
            if (sessionElement.TryGetProperty("key", out var keyElement))
            {
                SessionKey = keyElement.GetString();
                return true;
            }
        }
        return false;
    }

    public static void Logout()
    {
        SessionKey = null;
    }

    public static async Task UpdateNowPlayingAsync(string track, string artist)
    {
        if (!IsConnected || string.IsNullOrEmpty(SessionKey)) return;

        var parameters = new Dictionary<string, string>
        {
            { "method", "track.updateNowPlaying" },
            { "track", track },
            { "artist", artist },
            { "sk", SessionKey }
        };

        await ExecuteRequestAsync(parameters, HttpMethod.Post);
    }

    public static async Task ScrobbleAsync(string track, string artist, long timestampSeconds)
    {
        if (!IsConnected || string.IsNullOrEmpty(SessionKey)) return;

        var parameters = new Dictionary<string, string>
        {
            { "method", "track.scrobble" },
            { "track", track },
            { "artist", artist },
            { "timestamp", timestampSeconds.ToString() },
            { "sk", SessionKey }
        };

        await ExecuteRequestAsync(parameters, HttpMethod.Post);
    }

    public static string GetAuthUrl(string token)
    {
        return $"https://www.last.fm/api/auth/?api_key={ApiKey}&token={token}";
    }
}
