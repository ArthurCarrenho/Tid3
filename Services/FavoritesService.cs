using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TidalUi3.Models;

namespace TidalUi3.Services;

/// <summary>
/// What the user has in their collection — liked tracks, saved albums and playlists, followed
/// artists — loaded once after sign-in so any view can show and toggle it without a round trip
/// of its own. Used from the UI thread only.
/// </summary>
public static class FavoritesService
{
    private static readonly HashSet<int> _likedTrackIds = [];
    private static readonly HashSet<int> _savedAlbumIds = [];
    private static readonly HashSet<int> _followedArtistIds = [];
    private static readonly HashSet<string> _savedPlaylistIds = [];

    /// <summary>Raised whenever anything in the collection changes: after loading and after each toggle.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Raised for each track toggle with the track and its new state, also when a failed request
    /// rolls one back, so a list of liked songs can add or drop it in place.
    /// </summary>
    public static event Action<Track, bool>? TrackLikedChanged;

    /// <summary>Raised after a playlist was saved or removed on TIDAL, for lists of the library's playlists.</summary>
    public static event Action? LibraryPlaylistsChanged;

    public static bool IsTrackLiked(int trackId) => _likedTrackIds.Contains(trackId);

    /// <summary>Like <see cref="IsTrackLiked(int)"/>, also trusting a track known to be liked, such as one from the liked songs list.</summary>
    public static bool IsTrackLiked(Track track) => track.IsLiked || IsTrackLiked(track.Id);

    public static bool IsAlbumSaved(int albumId) => _savedAlbumIds.Contains(albumId);
    public static bool IsArtistFollowed(int artistId) => _followedArtistIds.Contains(artistId);
    public static bool IsPlaylistSaved(string uuid) => _savedPlaylistIds.Contains(uuid);

    public static async Task LoadAsync()
    {
        try
        {
            var ids = await App.ApiClient.GetFavoriteIdsAsync();
            Replace(_likedTrackIds, ids.Tracks);
            Replace(_savedAlbumIds, ids.Albums);
            Replace(_followedArtistIds, ids.Artists);
            Replace(_savedPlaylistIds, ids.Playlists);
            Changed?.Invoke();
        }
        catch
        {
            // Offline or not signed in: everything simply starts out not in the collection.
        }
    }

    /// <summary>
    /// Likes or unlikes a track on TIDAL. The local state flips immediately so the heart
    /// responds at once, and is rolled back if the request fails.
    /// </summary>
    public static async Task<bool> SetTrackLikedAsync(Track track, bool liked)
    {
        // No "already in that state" shortcut: if the set failed to load (offline at sign-in)
        // it says "not liked" for everything, and unliking from the library would do nothing.
        if (track.Id <= 0) return false;

        ApplyTrack(track, liked);
        try
        {
            if (liked)
                await App.ApiClient.AddFavoriteTrackAsync(track.Id);
            else
                await App.ApiClient.RemoveFavoriteTrackAsync(track.Id);
            return true;
        }
        catch
        {
            ApplyTrack(track, !liked);
            return false;
        }
    }

    public static Task<bool> SetAlbumSavedAsync(int albumId, bool saved) =>
        albumId <= 0 ? Task.FromResult(false) : ToggleAsync(_savedAlbumIds, albumId, saved,
            () => App.ApiClient.AddFavoriteAlbumAsync(albumId), () => App.ApiClient.RemoveFavoriteAlbumAsync(albumId));

    public static Task<bool> SetArtistFollowedAsync(int artistId, bool followed) =>
        artistId <= 0 ? Task.FromResult(false) : ToggleAsync(_followedArtistIds, artistId, followed,
            () => App.ApiClient.AddFavoriteArtistAsync(artistId), () => App.ApiClient.RemoveFavoriteArtistAsync(artistId));

    public static async Task<bool> SetPlaylistSavedAsync(string uuid, bool saved)
    {
        if (string.IsNullOrEmpty(uuid)) return false;
        var done = await ToggleAsync(_savedPlaylistIds, uuid, saved,
            () => App.ApiClient.AddFavoritePlaylistAsync(uuid), () => App.ApiClient.RemoveFavoritePlaylistAsync(uuid));
        // Only once TIDAL has it, so a list reloaded in response already includes the change.
        if (done) LibraryPlaylistsChanged?.Invoke();
        return done;
    }

    // Same optimistic flip-then-confirm as tracks, for the kinds that need no per-item event.
    private static async Task<bool> ToggleAsync<T>(HashSet<T> set, T id, bool on, Func<Task> add, Func<Task> remove)
    {
        Apply(set, id, on);
        try
        {
            await (on ? add() : remove());
            return true;
        }
        catch
        {
            Apply(set, id, !on);
            return false;
        }
    }

    private static void Apply<T>(HashSet<T> set, T id, bool on)
    {
        if (on) set.Add(id); else set.Remove(id);
        Changed?.Invoke();
    }

    private static void ApplyTrack(Track track, bool liked)
    {
        track.IsLiked = liked;
        Apply(_likedTrackIds, track.Id, liked);
        TrackLikedChanged?.Invoke(track, liked);
    }

    private static void Replace<T>(HashSet<T> target, HashSet<T> source)
    {
        target.Clear();
        target.UnionWith(source);
    }
}
