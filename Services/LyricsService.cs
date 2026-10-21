using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Models;

namespace TidalUi3.Services;

/// <summary>Lyrics of a track, one entry per line; synced lines carry their start time.</summary>
public sealed record TrackLyrics(IReadOnlyList<TidalLyricsSubtitle> Lines, bool IsInstrumental)
{
    public static readonly TrackLyrics None = new([], false);

    public bool IsSynced => Lines.Count > 0 && Lines[0].StartTimeMs != TidalLyricsSubtitle.Unsynced;
}

/// <summary>
/// Finds lyrics for a track. TIDAL's lyrics endpoint refuses most client ids, so the main
/// source is LRCLIB (lrclib.net), an open, keyless database of synced lyrics: an exact lookup
/// by title, artist, album and duration first, then a looser search keeping the result whose
/// length matches. TIDAL's endpoint stays as the last resort, in case it answers.
/// </summary>
public sealed class LyricsService
{
    private const string LrcLibUrl = "https://lrclib.net/api/";

    // A search hit this far off the track's length is taken for another recording (live,
    // extended mix, radio edit). LRCLIB's exact lookup itself allows ±2 s.
    private const double MaxDurationGapSeconds = 3;

    private readonly TidalApiClient _api;
    private readonly HttpClient _http;

    // Per track for this session, found or not, so replaying a track or reopening the panel
    // doesn't ask again. A miss is only stored once LRCLIB actually answered.
    private readonly ConcurrentDictionary<int, TrackLyrics> _cache = new();

    // Set once TIDAL refuses this client lyrics, which then holds for every track.
    private volatile bool _tidalRefused;

    public LyricsService(TidalApiClient api)
    {
        _api = api;
        _http = new HttpClient { BaseAddress = new Uri(LrcLibUrl), Timeout = TimeSpan.FromSeconds(15) };

        // LRCLIB asks clients to identify themselves.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tid3/{GetAppVersion()} (+https://github.com/ArthurCarrenho/Tid3)");
    }

    public async Task<TrackLyrics> GetLyricsAsync(Track track, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(track.Id, out var cached))
            return cached;

        var (lyrics, answered) = await FindOnLrcLibAsync(track, ct);
        lyrics ??= await FindOnTidalAsync(track.Id, ct);

        // While LRCLIB is overloaded a miss says nothing; leave it uncached to try again later.
        if (lyrics is not null || answered)
            _cache[track.Id] = lyrics ?? TrackLyrics.None;
        return lyrics ?? TrackLyrics.None;
    }

    // ── LRCLIB ──

    private async Task<(TrackLyrics? Lyrics, bool Answered)> FindOnLrcLibAsync(Track track, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
            return (null, true);

        var title = Uri.EscapeDataString(track.Title);
        var artist = Uri.EscapeDataString(track.Artist);

        // Exact lookup. LRCLIB requires all four fields, so it is skipped for tracks that lack
        // an album or a length, such as one started from a search suggestion.
        if (!string.IsNullOrWhiteSpace(track.Album) && track.DurationSeconds > 0)
        {
            var duration = (int)Math.Round(track.DurationSeconds);
            var (record, answered) = await GetJsonAsync<LrcLibRecord>(
                $"get?track_name={title}&artist_name={artist}&album_name={Uri.EscapeDataString(track.Album)}&duration={duration}", ct);
            if (!answered) return (null, false);
            if (record is not null && ToLyrics(record) is { } exact) return (exact, true);
        }

        // Looser search. Album names often differ between catalogs ("X - Single", "X (Deluxe)"),
        // so match on title and artist and let the length pick the recording.
        var (records, searched) = await GetJsonAsync<List<LrcLibRecord>>(
            $"search?track_name={title}&artist_name={artist}", ct);
        if (!searched) return (null, false);

        var best = (records ?? [])
            .Where(r => track.DurationSeconds <= 0 || Math.Abs(r.Duration - track.DurationSeconds) <= MaxDurationGapSeconds)
            .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
            .ThenBy(r => Math.Abs(r.Duration - track.DurationSeconds))
            .Select(ToLyrics)
            .FirstOrDefault(l => l is not null);
        return (best, true);
    }

    private static TrackLyrics? ToLyrics(LrcLibRecord record)
    {
        var synced = ParseLrc(record.SyncedLyrics);
        if (synced.Count > 0) return new(synced, false);

        var plain = ParsePlainLyrics(record.PlainLyrics);
        if (plain.Count > 0) return new(plain, false);

        return record.Instrumental ? new([], true) : null;
    }

    /// <summary>
    /// GETs and parses an LRCLIB endpoint. Answered is false when LRCLIB couldn't be reached
    /// or was overloaded; a 404 is an answer (nothing there).
    /// </summary>
    private async Task<(T? Value, bool Answered)> GetJsonAsync<T>(string relativeUrl, CancellationToken ct) where T : class
    {
        // LRCLIB is a community server and answers 503 when busy; that tends to clear within a
        // second, so one retry catches most of it.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(relativeUrl, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return (null, true);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    return (JsonSerializer.Deserialize<T>(json, TidalApiClient.JsonOptions), true);
                }

                // Any other client error won't change on a retry.
                if ((int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.TooManyRequests)
                    return (null, true);
            }
            catch (JsonException)
            {
                return (null, true);
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* timed out */ }

            if (attempt == 1)
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        return (null, false);
    }

    // ── TIDAL ──

    private async Task<TrackLyrics?> FindOnTidalAsync(int trackId, CancellationToken ct)
    {
        if (_tidalRefused || !_api.IsAuthenticated) return null;

        var (data, status) = await _api.GetTrackLyricsAsync(trackId, ct);
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            _tidalRefused = true;
        if (data is null) return null;

        var synced = ParseLrc(data.Subtitles);
        if (synced.Count > 0) return new(synced, false);

        var plain = ParsePlainLyrics(data.Lyrics);
        return plain.Count > 0 ? new(plain, false) : null;
    }

    // ── Parsing ──

    // LRC timestamps appear as [mm:ss], [mm:ss.ff] or [mm:ss.fff], with or without a
    // leading zero on the minutes. Listing the accepted shapes keeps the parser strict
    // enough that metadata tags such as [ar:Artist] still fall through as non-matches.
    private static readonly string[] LrcTimeFormats =
    [
        @"m\:ss", @"m\:ss\.f", @"m\:ss\.ff", @"m\:ss\.fff",
        @"mm\:ss", @"mm\:ss\.f", @"mm\:ss\.ff", @"mm\:ss\.fff",
    ];

    internal static List<TidalLyricsSubtitle> ParseLrc(string? lrc)
    {
        var result = new List<TidalLyricsSubtitle>();
        if (string.IsNullOrWhiteSpace(lrc)) return result;

        foreach (var line in lrc.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length == 0 || line[0] != '[') continue;

            var close = line.IndexOf(']');
            if (close < 2) continue;

            var timeStr = line.Substring(1, close - 1);
            if (!TimeSpan.TryParseExact(timeStr, LrcTimeFormats, CultureInfo.InvariantCulture, out var ts))
                continue;

            result.Add(new TidalLyricsSubtitle
            {
                StartTimeMs = (int)ts.TotalMilliseconds,
                // A bare timestamp with no text marks an instrumental gap; keep the entry
                // so the highlight can follow the song through it.
                Text = line[(close + 1)..].Trim()
            });
        }

        // The highlight lookup assumes chronological order.
        result.Sort((a, b) => a.StartTimeMs.CompareTo(b.StartTimeMs));
        return result;
    }

    internal static List<TidalLyricsSubtitle> ParsePlainLyrics(string? lyrics)
    {
        var result = new List<TidalLyricsSubtitle>();
        if (string.IsNullOrWhiteSpace(lyrics)) return result;

        foreach (var line in lyrics.Split('\n'))
        {
            result.Add(new TidalLyricsSubtitle
            {
                StartTimeMs = TidalLyricsSubtitle.Unsynced,
                Text = line.Trim(),
            });
        }
        return result;
    }

    private static string GetAppVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "1.0";
        }
    }
}
