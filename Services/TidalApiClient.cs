using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TidalUi3.Services;

public sealed class TidalApiClient : IDisposable
{
    private const string BaseUrl = "https://api.tidalhifi.com/v1/";
    private const string AuthUrl = "https://auth.tidal.com/v1/oauth2/";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;

    private string? _accessToken;
    private string? _refreshToken;
    private string _countryCode = "US";
    private int _userId;

    // Several requests can hit a 401 at once when the token expires. Only one of them may
    // refresh; the others wait and reuse the new token instead of refreshing again.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private const int PageSize = 100;
    private const int MaxPagedItems = 10_000;

    public string CountryCode
    {
        get => _countryCode;
        set => _countryCode = value;
    }

    public int UserId => _userId;
    public bool IsAuthenticated => _accessToken is not null;
    public string? AccessToken => _accessToken;

    /// <summary>
    /// Raised whenever the token state changes (login, refresh, manual set).
    /// </summary>
    public event Action<string, string?, int, string>? TokenChanged;

    public TidalApiClient(string clientId, string clientSecret)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        // The default 100 s would leave a track "loading" for minutes on a dead connection.
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    // ??????????????????????????????????????????????
    //  Authentication � Device Code Flow
    // ??????????????????????????????????????????????

    /// <summary>
    /// Starts the device authorization flow. Returns the user code and
    /// verification URI to display to the user.
    /// </summary>
    public async Task<DeviceAuthResponse> StartDeviceAuthAsync(CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = "r_usr+w_usr+w_sub"
        });

        using var response = await _http.PostAsync($"{AuthUrl}device_authorization", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<DeviceAuthResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse device auth response.");
    }

    /// <summary>
    /// Polls for the device code to be authorized. Returns a <see cref="TokenResponse"/>
    /// once the user has authorized, or <c>null</c> if still pending.
    /// Throws on denial or expiry.
    /// </summary>
    public async Task<TokenResponse?> PollDeviceTokenAsync(string deviceCode, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["device_code"] = deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["scope"] = "r_usr+w_usr+w_sub"
        });

        using var response = await _http.PostAsync($"{AuthUrl}token", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorJson = await response.Content.ReadAsStringAsync(ct);
            using var errorDoc = JsonDocument.Parse(errorJson);
            var error = errorDoc.RootElement.TryGetProperty("error", out var errorProp)
                ? errorProp.GetString() : null;

            return error switch
            {
                "authorization_pending" => null,
                "expired_token" => throw new InvalidOperationException("Device code has expired."),
                "access_denied" => throw new UnauthorizedAccessException("User denied the authorization request."),
                _ => throw new HttpRequestException($"Token request failed: {error ?? errorJson}")
            };
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse token response.");

        ApplyToken(token);
        return token;
    }

    /// <summary>
    /// Refreshes an expired access token using a stored refresh token.
    /// </summary>
    public async Task<TokenResponse> RefreshAccessTokenAsync(CancellationToken ct = default)
    {
        if (_refreshToken is null)
            throw new InvalidOperationException("No refresh token available.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["refresh_token"] = _refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = "r_usr+w_usr+w_sub"
        });

        using var response = await _http.PostAsync($"{AuthUrl}token", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse token response.");

        ApplyToken(token);
        return token;
    }

    // ??????????????????????????????????????????????
    //  User Data
    // ??????????????????????????????????????????????

    public async Task<TidalUser> GetUserProfileAsync(CancellationToken ct = default)
    {
        EnsureUserId();
        return await GetAsync<TidalUser>($"users/{_userId}?countryCode={_countryCode}", ct);
    }

    /// <summary>
    /// Manually sets the access token (e.g. restored from storage).
    /// </summary>
    public void SetToken(string accessToken, string? refreshToken = null, int userId = 0, string countryCode = "US")
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _userId = userId;
        _countryCode = countryCode;
    }

    /// <summary>
    /// Forgets the tokens held in memory. Clearing the stored copy is up to the caller
    /// (TokenStorageService); without this, a refresh racing the sign-out would save them again.
    /// </summary>
    public void SignOut()
    {
        _accessToken = null;
        _refreshToken = null;
        _userId = 0;
    }

    /// <summary>
    /// Fills in the account id and country when the stored session lacked them, keeping the
    /// current tokens, and persists the result.
    /// </summary>
    public void SetUser(int userId, string? countryCode)
    {
        _userId = userId;
        if (!string.IsNullOrEmpty(countryCode))
            _countryCode = countryCode;
        if (_accessToken is not null)
            TokenChanged?.Invoke(_accessToken, _refreshToken, _userId, _countryCode);
    }

    // ??????????????????????????????????????????????
    //  Search
    // ??????????????????????????????????????????????

    /// <summary>
    /// Searches across tracks, albums, artists, and playlists.
    /// </summary>
    public async Task<TidalSearchResult> SearchAsync(
        string query, int limit = 25, int offset = 0,
        string types = "ARTISTS,ALBUMS,TRACKS,PLAYLISTS",
        CancellationToken ct = default)
    {
        var url = $"search?query={Uri.EscapeDataString(query)}&types={types}" +
                  $"&limit={limit}&offset={offset}&countryCode={_countryCode}";
        return await GetAsync<TidalSearchResult>(url, ct);
    }

    /// <summary>
    /// Lightweight search for live suggestions while typing.
    /// </summary>
    public async Task<TidalTopHitsResult> SearchTopHitsAsync(
        string query, int limit = 3, CancellationToken ct = default)
    {
        var url = $"search/top-hits?query={Uri.EscapeDataString(query)}" +
                  $"&limit={limit}&countryCode={_countryCode}";
        return await GetAsync<TidalTopHitsResult>(url, ct);
    }

    // ──────────────────────────────────────────────
    //  Raw JSON (for debugging / logging)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Debug-only: authenticated request to any absolute URL. Supports all HTTP methods, custom headers, and a request body.
    /// </summary>
    public async Task<(string Body, int StatusCode)> DebugGetAsync(string absoluteUrl, CancellationToken ct = default)
        => await DebugRequestAsync(HttpMethod.Get, absoluteUrl, null, null, null, ct);

    public async Task<(string Body, int StatusCode)> DebugRequestAsync(
        HttpMethod method,
        string absoluteUrl,
        IEnumerable<(string Key, string Value)>? headers = null,
        string? body = null,
        string? contentType = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, absoluteUrl);
        if (_accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (headers != null)
            foreach (var (key, value) in headers)
                request.Headers.TryAddWithoutValidation(key, value);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8,
                contentType ?? "application/x-www-form-urlencoded");
        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return (responseBody, (int)response.StatusCode);
    }

    // ──────────────────────────────────────────────
    //  For You
    // ──────────────────────────────────────────────

    public async Task<System.Text.Json.JsonElement> GetForYouPageAsync(CancellationToken ct = default)
    {
        return await GetAsync<System.Text.Json.JsonElement>($"pages/for_you?countryCode={_countryCode}&deviceType=BROWSER", ct);
    }

    public async Task<System.Text.Json.JsonElement> GetRawAsync(string url, CancellationToken ct = default)
    {
        return await GetAsync<System.Text.Json.JsonElement>(url, ct);
    }

    // ??????????????????????????????????????????????
    //  Tracks
    // ??????????????????????????????????????????????

    public async Task<TidalTrack> GetTrackAsync(int trackId, CancellationToken ct = default)
    {
        return await GetAsync<TidalTrack>($"tracks/{trackId}?countryCode={_countryCode}", ct);
    }

    public async Task<TidalStreamUrl> GetStreamUrlAsync(
        int trackId, string quality = "LOSSLESS", CancellationToken ct = default)
    {
        return await GetAsync<TidalStreamUrl>(
            $"tracks/{trackId}/streamUrl?soundQuality={quality}", ct);
    }

    /// <summary>
    /// Gets playback info including a base64-encoded manifest with stream URLs.
    /// Quality options: LOW, HIGH, LOSSLESS, HI_RES, HI_RES_LOSSLESS.
    /// </summary>
    public async Task<TidalPlaybackInfo> GetPlaybackInfoAsync(
        int trackId, string quality = "HI_RES_LOSSLESS",
        string playbackMode = "STREAM", string assetPresentation = "FULL",
        CancellationToken ct = default)
    {
        return await GetAsync<TidalPlaybackInfo>(
            $"tracks/{trackId}/playbackinfo?audioquality={quality}" +
            $"&playbackmode={playbackMode}&assetpresentation={assetPresentation}", ct);
    }

    /// <summary>
    /// Decodes the base64 manifest from <see cref="TidalPlaybackInfo"/> into stream URLs.
    /// </summary>
    public static TidalManifest DecodeManifest(TidalPlaybackInfo playbackInfo)
    {
        var decoded = System.Text.Encoding.UTF8.GetString(
            System.Convert.FromBase64String(playbackInfo.Manifest));

        // DASH XML manifest (used for HI_RES_LOSSLESS / FLAC tracks)
        if (playbackInfo.ManifestMimeType == "application/dash+xml" || decoded.TrimStart().StartsWith("<"))
        {
            return ParseDashManifest(decoded);
        }

        // Regular JSON manifest
        return System.Text.Json.JsonSerializer.Deserialize<TidalManifest>(decoded, JsonOptions)
            ?? throw new System.InvalidOperationException("Failed to decode playback manifest.");
    }

    private static TidalManifest ParseDashManifest(string xml)
    {
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);

        var nsMgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("mpd", "urn:mpeg:dash:schema:mpd:2011");

        // Extract codec from Representation
        var rep = doc.SelectSingleNode("//mpd:Representation", nsMgr);
        var codec = rep?.Attributes?["codecs"]?.Value ?? "FLAC";

        // Extract initialization URL from SegmentTemplate
        var segTemplate = doc.SelectSingleNode("//mpd:SegmentTemplate", nsMgr);
        var initUrl = segTemplate?.Attributes?["initialization"]?.Value;

        if (string.IsNullOrEmpty(initUrl))
            throw new System.InvalidOperationException("DASH manifest does not contain a stream URL.");

        // Clean up XML-encoded ampersands
        initUrl = initUrl.Replace("&amp;", "&");

        return new TidalManifest
        {
            MimeType = "audio/mp4",
            Codecs = codec,
            Urls = new List<string> { initUrl }
        };
    }

    // ??????????????????????????????????????????????
    //  Albums
    // ??????????????????????????????????????????????

    public async Task<TidalAlbum> GetAlbumAsync(int albumId, CancellationToken ct = default)
    {
        return await GetAsync<TidalAlbum>($"albums/{albumId}?countryCode={_countryCode}", ct);
    }

    public async Task<TidalPagedResult<TidalTrack>> GetAlbumTracksAsync(
        int albumId, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        return await GetAsync<TidalPagedResult<TidalTrack>>(
            $"albums/{albumId}/tracks?limit={limit}&offset={offset}&countryCode={_countryCode}", ct);
    }

    // ??????????????????????????????????????????????
    //  Artists
    // ??????????????????????????????????????????????

    public async Task<TidalArtist> GetArtistAsync(int artistId, CancellationToken ct = default)
    {
        return await GetAsync<TidalArtist>($"artists/{artistId}?countryCode={_countryCode}", ct);
    }

    public async Task<TidalPagedResult<TidalTrack>> GetArtistTopTracksAsync(
        int artistId, int limit = 25, int offset = 0, CancellationToken ct = default)
    {
        return await GetAsync<TidalPagedResult<TidalTrack>>(
            $"artists/{artistId}/toptracks?limit={limit}&offset={offset}&countryCode={_countryCode}", ct);
    }

    public async Task<TidalPagedResult<TidalAlbum>> GetArtistAlbumsAsync(
        int artistId, int limit = 25, int offset = 0, CancellationToken ct = default)
    {
        return await GetAsync<TidalPagedResult<TidalAlbum>>(
            $"artists/{artistId}/albums?limit={limit}&offset={offset}&countryCode={_countryCode}", ct);
    }

    public async Task<TidalArtistBio?> GetArtistBioAsync(int artistId, CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<TidalArtistBio>($"artists/{artistId}/bio?countryCode={_countryCode}", ct);
        }
        catch { return null; }
    }

    public async Task<TidalPagedResult<TidalAlbum>> GetArtistSinglesAsync(
        int artistId, int limit = 25, int offset = 0, CancellationToken ct = default)
    {
        return await GetAsync<TidalPagedResult<TidalAlbum>>(
            $"artists/{artistId}/albums?filter=EPSANDSINGLES&limit={limit}&offset={offset}&countryCode={_countryCode}", ct);
    }

    public async Task<TidalPagedResult<TidalAlbum>> GetArtistAppearsOnAsync(
        int artistId, int limit = 25, int offset = 0, CancellationToken ct = default)
    {
        return await GetAsync<TidalPagedResult<TidalAlbum>>(
            $"artists/{artistId}/albums?filter=COMPILATIONS&limit={limit}&offset={offset}&countryCode={_countryCode}", ct);
    }

    // ---------- Lyrics  -------------------------------------------------------

    /// <summary>
    /// TIDAL's own lyrics for a track, unparsed, with the status code (null when the request
    /// didn't get through). Most client ids are refused here; see LyricsService for the
    /// sources actually relied on.
    /// </summary>
    public async Task<(TidalLyricsData? Lyrics, System.Net.HttpStatusCode? Status)> GetTrackLyricsAsync(
        int trackId, CancellationToken ct = default)
    {
        try
        {
            EnsureAuthenticated();
            var url = $"https://api.tidal.com/v1/tracks/{trackId}/lyrics?countryCode={_countryCode}";

            // Not SendAuthorizedAsync: a 401 here means this client isn't allowed lyrics, not
            // that the token expired, and refreshing it on every track would be pointless.
            using var response = await SendWithTokenAsync(new HttpRequestMessage(HttpMethod.Get, url), _accessToken, ct);
            if (!response.IsSuccessStatusCode) return (null, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync(ct);
            return (JsonSerializer.Deserialize<TidalLyricsData>(json, JsonOptions), response.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Superseded by a newer request — let the caller discard this result.
            throw;
        }
        catch
        {
            return (null, null);
        }
    }

    // ??????????????????????????????????????????????
    //  Playlists
    // ??????????????????????????????????????????????

    public async Task<TidalPlaylist> GetPlaylistAsync(string playlistUuid, CancellationToken ct = default)
    {
        return await GetAsync<TidalPlaylist>($"playlists/{playlistUuid}?countryCode={_countryCode}", ct);
    }

    public async Task<TidalPagedResult<TidalTrackItem>> GetPlaylistTracksAsync(
        string playlistUuid, int limit = 100, int offset = 0, CancellationToken ct = default)
    {
        return await GetAsync<TidalPagedResult<TidalTrackItem>>(
            $"playlists/{playlistUuid}/items?limit={limit}&offset={offset}&countryCode={_countryCode}", ct);
    }

    /// <summary>
    /// Every item of a playlist, one page at a time, so the first tracks can be shown while
    /// the rest of a long playlist is still loading.
    /// </summary>
    public IAsyncEnumerable<List<TidalTrackItem>> GetPlaylistTrackPagesAsync(string playlistUuid, CancellationToken ct = default)
        => GetPagesAsync(offset => GetPlaylistTracksAsync(playlistUuid, PageSize, offset, ct));

    // =========================================================================
    //  Mixes
    // =========================================================================

    /// <summary>
    /// A mix's header and its tracks. Both come from the same pages/mix response, which used
    /// to be fetched twice, once for each.
    /// </summary>
    public async Task<(TidalPlaylist Info, List<TidalTrackItem> Tracks)> GetMixAsync(string mixId, CancellationToken ct = default)
    {
        var json = await GetAsync<JsonElement>($"pages/mix?mixId={mixId}&countryCode={_countryCode}&deviceType=BROWSER", ct);
        return (ParseMixHeader(mixId, json), ParseMixTracks(json));
    }

    private static TidalPlaylist ParseMixHeader(string mixId, JsonElement json)
    {
        var playlist = new TidalPlaylist { Uuid = mixId };

        if (json.TryGetProperty("rows", out var rows) && rows.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.TryGetProperty("modules", out var modules) && modules.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var module in modules.EnumerateArray())
                    {
                        if (module.TryGetProperty("type", out var type) && type.GetString() == "MIX_HEADER")
                        {
                            if (module.TryGetProperty("mix", out var mix))
                            {
                                if (mix.TryGetProperty("title", out var titleObj))
                                    playlist.Title = titleObj.GetString() ?? "";
                                if (mix.TryGetProperty("subTitle", out var subTitleObj))
                                    playlist.Description = subTitleObj.GetString() ?? "";

                                if (mix.TryGetProperty("images", out var images) && images.ValueKind == System.Text.Json.JsonValueKind.Object && images.TryGetProperty("LARGE", out var large) && large.ValueKind == System.Text.Json.JsonValueKind.Object && large.TryGetProperty("url", out var urlStr))
                                {
                                    playlist.Image = urlStr.GetString() ?? string.Empty;
                                }
                                else if (mix.TryGetProperty("images", out var mdImages) && mdImages.ValueKind == System.Text.Json.JsonValueKind.Object && mdImages.TryGetProperty("MEDIUM", out var medium) && medium.ValueKind == System.Text.Json.JsonValueKind.Object && medium.TryGetProperty("url", out var mdUrlStr))
                                {
                                    playlist.Image = mdUrlStr.GetString() ?? string.Empty;
                                }
                            }
                        }
                    }
                }
            }
        }
        return playlist;
    }

    private static List<TidalTrackItem> ParseMixTracks(JsonElement pageJson)
    {
        // Mix API returns bare TidalTrack objects in pagedList.items (not wrapped in TidalTrackItem).
        // Extract them directly from the initial page JSON and wrap them.
        if (pageJson.TryGetProperty("rows", out var rows) && rows.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.TryGetProperty("modules", out var modules) && modules.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var module in modules.EnumerateArray())
                    {
                        if (module.TryGetProperty("type", out var type) && type.GetString() == "TRACK_LIST")
                        {
                            if (module.TryGetProperty("pagedList", out var pagedList) && pagedList.TryGetProperty("items", out var items))
                            {
                                var tracks = new List<TidalTrackItem>();
                                foreach (var item in items.EnumerateArray())
                                {
                                    var track = System.Text.Json.JsonSerializer.Deserialize<TidalTrack>(item.GetRawText(), JsonOptions);
                                    if (track != null)
                                        tracks.Add(new TidalTrackItem { Item = track });
                                }
                                return tracks;
                            }
                        }
                    }
                }
            }
        }

        return [];
    }

    // ??????????????????????????????????????????????
    //  Favorites (requires userId)
    // ??????????????????????????????????????????????

    public async Task<TidalPagedResult<TidalTrackItem>> GetFavoriteTracksAsync(
        int limit = 50, int offset = 0, string order = "DATE", string orderDirection = "DESC",
        CancellationToken ct = default)
    {
        EnsureUserId();
        return await GetAsync<TidalPagedResult<TidalTrackItem>>(
            $"users/{_userId}/favorites/tracks?limit={limit}&offset={offset}" +
            $"&order={order}&orderDirection={orderDirection}&countryCode={_countryCode}", ct);
    }

    /// <summary>Every favorited track, newest first, one page at a time.</summary>
    public IAsyncEnumerable<List<TidalTrackItem>> GetFavoriteTrackPagesAsync(CancellationToken ct = default)
        => GetPagesAsync(offset => GetFavoriteTracksAsync(PageSize, offset, ct: ct));

    /// <summary>
    /// Ids of every favorited track. Uses the compact favorites/ids listing, and falls back
    /// to paging through the full favorites if that endpoint is unavailable.
    /// </summary>
    public async Task<HashSet<int>> GetFavoriteTrackIdsAsync(CancellationToken ct = default)
    {
        EnsureUserId();
        try
        {
            var json = await GetAsync<JsonElement>($"users/{_userId}/favorites/ids?countryCode={_countryCode}", ct);
            if (json.TryGetProperty("TRACK", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
            {
                var ids = new HashSet<int>();
                foreach (var el in tracks.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var number))
                        ids.Add(number);
                    else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out number))
                        ids.Add(number);
                }
                return ids;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* fall back to paging below */ }

        var all = await GetAllPagesAsync(offset => GetFavoriteTracksAsync(PageSize, offset, ct: ct));
        var result = new HashSet<int>();
        foreach (var item in all)
            if (item.Item is not null) result.Add(item.Item.Id);
        return result;
    }

    public async Task AddFavoriteTrackAsync(int trackId, CancellationToken ct = default)
    {
        EnsureUserId();
        using var response = await SendAuthorizedAsync(() => new HttpRequestMessage(
            HttpMethod.Post, $"users/{_userId}/favorites/tracks")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["trackIds"] = trackId.ToString(),
                ["countryCode"] = _countryCode
            })
        }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveFavoriteTrackAsync(int trackId, CancellationToken ct = default)
    {
        EnsureUserId();
        using var response = await SendAuthorizedAsync(() => new HttpRequestMessage(
            HttpMethod.Delete, $"users/{_userId}/favorites/tracks/{trackId}?countryCode={_countryCode}"), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TidalPagedResult<TidalAlbumItem>> GetFavoriteAlbumsAsync(
        int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        EnsureUserId();
        return await GetAsync<TidalPagedResult<TidalAlbumItem>>(
            $"users/{_userId}/favorites/albums?limit={limit}&offset={offset}&countryCode={_countryCode}", ct);
    }

    public async Task<TidalPagedResult<TidalPlaylist>> GetFavoritePlaylistsAsync(
        int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        EnsureUserId();
        return await GetAsync<TidalPagedResult<TidalPlaylist>>(
            $"users/{_userId}/playlists?limit={limit}&offset={offset}&countryCode={_countryCode}", ct);
    }

    // ??????????????????????????????????????????????
    //  Image helpers
    // ??????????????????????????????????????????????

    /// <summary>
    /// Builds a cover image URL from a Tidal image id.
    /// Typical sizes: 80x80, 160x160, 320x320, 640x640, 1280x1280.
    /// </summary>
    public static string GetImageUrl(string? imageId, int width = 320, int height = 320)
    {
        if (string.IsNullOrEmpty(imageId))
            return string.Empty;
        if (imageId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return imageId;
        var path = imageId.Replace('-', '/');
        return $"https://resources.tidal.com/images/{path}/{width}x{height}.jpg";
    }

    // ??????????????????????????????????????????????
    //  Internals
    // ??????????????????????????????????????????????

    public async Task<TidalPagedResult<TidalTrack>> GetTrackRadioAsync(int trackId, int limit = 50, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        // Fallback or explicit tracks/{id}/radio endpoint
        // It returns track items directly, not wrapped in an "item" object
        return await GetAsync<TidalPagedResult<TidalTrack>>(
            $"tracks/{trackId}/radio?limit={limit}&countryCode={_countryCode}", ct);
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        if (!relativeUrl.Contains("locale="))
        {
            var locale = System.Linq.Enumerable.FirstOrDefault(Windows.Globalization.ApplicationLanguages.Languages)?.Replace("-", "_") ?? "en_US";
            relativeUrl += (relativeUrl.Contains("?") ? "&" : "?") + $"locale={locale}";
        }

        // GETs are safe to repeat, and one hiccup (a 503, a rate limit, a dropped connection)
        // shouldn't fail a playlist that is fifty pages deep.
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, relativeUrl), ct);
            }
            catch (HttpRequestException) when (attempt < MaxGetAttempts && !ct.IsCancellationRequested)
            {
                await Task.Delay(GetRetryDelay(attempt, null), ct);
                continue;
            }

            using (response)
            {
                if (IsTransient(response.StatusCode) && attempt < MaxGetAttempts)
                {
                    await Task.Delay(GetRetryDelay(attempt, response.Headers.RetryAfter), ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<T>(json, JsonOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize response from {relativeUrl}");
            }
        }
    }

    private const int MaxGetAttempts = 3;

    private static bool IsTransient(System.Net.HttpStatusCode status) => status is
        System.Net.HttpStatusCode.TooManyRequests or
        System.Net.HttpStatusCode.InternalServerError or
        System.Net.HttpStatusCode.BadGateway or
        System.Net.HttpStatusCode.ServiceUnavailable or
        System.Net.HttpStatusCode.GatewayTimeout;

    // Honour a short Retry-After from a rate limit, otherwise back off 1 s, then 2 s.
    private static TimeSpan GetRetryDelay(int attempt, RetryConditionHeaderValue? retryAfter) =>
        retryAfter?.Delta is { } delta && delta <= TimeSpan.FromSeconds(10)
            ? delta
            : TimeSpan.FromSeconds(attempt);

    /// <summary>
    /// Sends a request with the current access token, refreshing it and retrying once on a
    /// 401. The token goes on each request rather than on DefaultRequestHeaders, which must
    /// not be modified while other requests are in flight.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        EnsureAuthenticated();

        var tokenUsed = _accessToken;
        var response = await SendWithTokenAsync(createRequest(), tokenUsed, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized || _refreshToken is null)
            return response;

        response.Dispose();
        await RefreshOnceAsync(tokenUsed, ct);
        return await SendWithTokenAsync(createRequest(), _accessToken, ct);
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpRequestMessage request, string? token, CancellationToken ct)
    {
        using (request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await _http.SendAsync(request, ct);
        }
    }

    private async Task RefreshOnceAsync(string? rejectedToken, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            // Another request already refreshed while this one waited for the lock.
            if (_accessToken != rejectedToken) return;
            await RefreshAccessTokenAsync(ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Walks every page of a paged endpoint. Stops at the reported total, at a short or
    /// empty page, or at <see cref="MaxPagedItems"/> as a safety net.
    /// </summary>
    private static async IAsyncEnumerable<List<T>> GetPagesAsync<T>(Func<int, Task<TidalPagedResult<T>>> fetchPage)
    {
        var loaded = 0;
        while (loaded < MaxPagedItems)
        {
            var page = await fetchPage(loaded);
            if (page.Items is not { Count: > 0 }) yield break;
            loaded += page.Items.Count;
            yield return page.Items;

            var reachedEnd = page.TotalNumberOfItems > 0
                ? loaded >= page.TotalNumberOfItems
                : page.Items.Count < PageSize;
            if (reachedEnd) yield break;
        }
    }

    private static async Task<List<T>> GetAllPagesAsync<T>(Func<int, Task<TidalPagedResult<T>>> fetchPage)
    {
        var all = new List<T>();
        await foreach (var page in GetPagesAsync(fetchPage))
            all.AddRange(page);
        return all;
    }

    private void ApplyToken(TokenResponse token)
    {
        _accessToken = token.AccessToken;
        if (!string.IsNullOrEmpty(token.RefreshToken))
            _refreshToken = token.RefreshToken;
        if (token.User is not null)
        {
            _userId = token.User.UserId;
            if (!string.IsNullOrEmpty(token.User.CountryCode))
                _countryCode = token.User.CountryCode;
        }
        TokenChanged?.Invoke(_accessToken, _refreshToken, _userId, _countryCode);
    }

    private void EnsureAuthenticated()
    {
        if (_accessToken is null)
            throw new InvalidOperationException(
                "Not authenticated. Call StartDeviceAuthAsync/PollDeviceTokenAsync or SetToken first.");
    }

    private void EnsureUserId()
    {
        EnsureAuthenticated();
        if (_userId == 0)
            throw new InvalidOperationException("User ID is not set. Complete authentication first.");
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
