using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TidalUi3.Models;
using TidalUi3.Services;
using TidalUi3.Helpers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TidalUi3.Pages;

public sealed partial class LibraryPage : Page
{
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private bool _loaded;
    private List<Track>? _likedTracks;

    public LibraryPage()
    {
        InitializeComponent();
        Loaded += LibraryPage_Loaded;
    }

    // ─── Data Loading ──────────────────────────────────────────

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        if (!_api.IsAuthenticated)
            return;

        LibraryLoading.IsLoading = true;

        try
        {
            var playlistsTask = _api.GetFavoritePlaylistsAsync(limit: 20);
            var albumsTask = _api.GetFavoriteAlbumsAsync(limit: 20);
            var tracksTask = _api.GetFavoriteTracksAsync(limit: 50);

            await System.Threading.Tasks.Task.WhenAll(playlistsTask, albumsTask, tracksTask);

            // Playlists
            var playlists = await playlistsTask;
            var playlistItems = playlists.Items
                .Where(i => i is not null)
                .Select(i => MapPlaylist(i!))
                .ToList();
            UserPlaylistsGrid.ItemsSource = new ObservableCollection<Playlist>(playlistItems);
            PlaylistsSection.Visibility = playlistItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Albums
            var albums = await albumsTask;
            var albumItems = albums.Items
                .Where(i => i.Item is not null)
                .Select(i => MapAlbum(i.Item!))
                .ToList();
            AlbumsCarousel.ItemsSource = new ObservableCollection<Album>(albumItems);
            AlbumsCarousel.Visibility = albumItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Liked Tracks
            var tracks = await tracksTask;
            var trackIndex = 1;
            _likedTracks = tracks.Items
                .Where(i => i.Item is not null)
                .Select(i => MapTrack(i.Item!, trackIndex++))
                .ToList();
            LikedSongsList.ItemsSource = new ObservableCollection<Track>(_likedTracks);
            LikedSongsSection.Visibility = _likedTracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            PlaylistsSection.Visibility = Visibility.Collapsed;
            AlbumsCarousel.Visibility = Visibility.Collapsed;
            LikedSongsSection.Visibility = Visibility.Collapsed;
        }
        finally
        {
            LibraryLoading.IsLoading = false;
        }
    }

    // ─── Click Handlers ────────────────────────────────────────

    private void PlaylistItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist && !string.IsNullOrEmpty(playlist.Uuid))
            Frame.Navigate(typeof(PlaylistDetailPage), playlist.Uuid);
    }

    private void AlbumItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Album album && album.Id > 0)
            Frame.Navigate(typeof(PlaylistDetailPage), album.Id);
    }

    private void TrackItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track && _likedTracks is not null)
        {
            var idx = _likedTracks.IndexOf(track);
            _queue.PlayTracks(_likedTracks, idx >= 0 ? idx : 0);
        }
    }

    private void TrackItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        => TrackContextMenu.Show(sender, e, _queue, (t, p) => Frame.Navigate(t, p));

    // ─── Mappers ───────────────────────────────────────────────

    private static Track MapTrack(TidalTrack t, int index)
    {
        var track = TrackMapper.Map(t, index);
        track.IsLiked = true;
        return track;
    }

    private static Album MapAlbum(TidalAlbum a)
    {
        var loader = new ResourceLoader();
        return new()
        {
            Id = a.Id,
            Title = a.Title,
            Artist = a.Artist?.Name ?? a.Artists.FirstOrDefault()?.Name ?? loader.GetString("Library_UnknownArtist"),
            Year = a.ReleaseDate?[..4] ?? "",
            CoverUrl = TidalApiClient.GetImageUrl(a.Cover, 320, 320)
        };
    }

    private static Playlist MapPlaylist(TidalPlaylist p) => new()
    {
        Uuid = p.Uuid,
        Title = p.Title,
        Description = p.Description,
        TrackCount = p.NumberOfTracks,
        Duration = FormatHelper.FormatPlaylistDuration(p.Duration),
        CoverUrl = TidalApiClient.GetImageUrl(p.SquareImage ?? p.Image, 320, 320)
    };

}
