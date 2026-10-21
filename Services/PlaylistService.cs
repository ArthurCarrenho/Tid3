using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TidalUi3.Services;

/// <summary>
/// The user's own playlists, the ones tracks can be added to, kept in memory so the "Add to
/// playlist" menu opens instantly. Loaded after sign-in and updated as playlists are created.
/// Used from the UI thread only.
/// </summary>
public static class PlaylistService
{
    private static readonly List<TidalPlaylist> _ownPlaylists = [];

    public static IReadOnlyList<TidalPlaylist> OwnPlaylists => _ownPlaylists;

    /// <summary>Raised when the list of own playlists changes: loaded, or one created.</summary>
    public static event Action? Changed;

    /// <summary>Raised after a playlist's tracks changed, with its uuid, so an open page of it can reload.</summary>
    public static event Action<string>? PlaylistEdited;

    public static bool IsOwn(TidalPlaylist playlist) =>
        App.ApiClient.UserId != 0 && playlist.Creator?.Id == App.ApiClient.UserId;

    public static async Task LoadAsync()
    {
        try
        {
            var all = new List<TidalPlaylist>();
            await foreach (var page in App.ApiClient.GetOwnPlaylistPagesAsync())
                all.AddRange(page);

            _ownPlaylists.Clear();
            _ownPlaylists.AddRange(all);
            Changed?.Invoke();
        }
        catch
        {
            // Offline: the menu just offers "New playlist" until the next sign-in.
        }
    }

    public static async Task<TidalPlaylist> CreateAsync(string title)
    {
        var playlist = await App.ApiClient.CreatePlaylistAsync(title);
        _ownPlaylists.Insert(0, playlist);
        Changed?.Invoke();
        return playlist;
    }

    public static async Task AddTracksAsync(string uuid, IEnumerable<int> trackIds)
    {
        await App.ApiClient.AddTracksToPlaylistAsync(uuid, trackIds);
        PlaylistEdited?.Invoke(uuid);
    }

    public static async Task RemoveItemAsync(string uuid, int index)
    {
        await App.ApiClient.RemovePlaylistItemAsync(uuid, index);
        PlaylistEdited?.Invoke(uuid);
    }
}
