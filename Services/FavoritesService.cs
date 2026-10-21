using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TidalUi3.Models;

namespace TidalUi3.Services;

/// <summary>
/// The set of tracks the user has liked, loaded once after sign-in so any view can show
/// and toggle the heart without a round trip of its own. Used from the UI thread only.
/// </summary>
public static class FavoritesService
{
    private static readonly HashSet<int> _likedTrackIds = [];

    /// <summary>Raised whenever the liked set changes: after loading and after each toggle.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Raised for each toggle with the track and its new state, also when a failed request
    /// rolls one back, so a list of liked songs can add or drop it in place.
    /// </summary>
    public static event Action<Track, bool>? TrackLikedChanged;

    public static bool IsTrackLiked(int trackId) => _likedTrackIds.Contains(trackId);

    /// <summary>Like <see cref="IsTrackLiked(int)"/>, also trusting a track known to be liked, such as one from the liked songs list.</summary>
    public static bool IsTrackLiked(Track track) => track.IsLiked || IsTrackLiked(track.Id);

    public static async Task LoadAsync()
    {
        try
        {
            var ids = await App.ApiClient.GetFavoriteTrackIdsAsync();
            _likedTrackIds.Clear();
            _likedTrackIds.UnionWith(ids);
            Changed?.Invoke();
        }
        catch
        {
            // Offline or not signed in: hearts simply start out empty.
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

        Apply(track, liked);
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
            Apply(track, !liked);
            return false;
        }
    }

    private static void Apply(Track track, bool liked)
    {
        if (liked)
            _likedTrackIds.Add(track.Id);
        else
            _likedTrackIds.Remove(track.Id);
        track.IsLiked = liked;
        Changed?.Invoke();
        TrackLikedChanged?.Invoke(track, liked);
    }
}
