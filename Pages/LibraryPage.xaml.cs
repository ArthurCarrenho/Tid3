using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Models;
using TidalUi3.Services;
using TidalUi3.Helpers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TidalUi3.Pages;

public sealed partial class LibraryPage : Page
{
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private readonly ObservableCollection<Track> _likedTracks = [];
    private readonly HashSet<int> _likedTrackIds = [];
    // Unliked while the list was still loading: a later page may still contain them.
    private readonly HashSet<int> _unlikedTrackIds = [];
    private readonly LoadingTrackListPlayer _player;
    private readonly CancellationTokenSource _loadCts = new();
    private bool _loaded;

    public LibraryPage()
    {
        InitializeComponent();
        _player = new(_queue);
        LikedSongsList.ItemsSource = _likedTracks;
        Loaded += LibraryPage_Loaded;
        FavoritesService.TrackLikedChanged += OnTrackLikedChanged;

        Unloaded += (_, _) =>
        {
            // Pages aren't cached, so once this one is gone nobody sees the rest of the list.
            _loadCts.Cancel();
            FavoritesService.TrackLikedChanged -= OnTrackLikedChanged;
        };
    }

    // ─── Data Loading ──────────────────────────────────────────

    private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        if (!_api.IsAuthenticated)
            return;

        LibraryLoading.IsLoading = true;

        // The first page of liked songs comes in with the rest of the library. Later pages
        // keep arriving after the overlay is gone, while the list can already be used.
        _player.BeginLoading();
        var likedPages = _api.GetFavoriteTrackPagesAsync(_loadCts.Token).GetAsyncEnumerator();
        var gotFirstPage = false;

        try
        {
            var playlistsTask = _api.GetFavoritePlaylistsAsync(limit: 20);
            var albumsTask = _api.GetFavoriteAlbumsAsync(limit: 20);
            var tracksTask = likedPages.MoveNextAsync().AsTask();

            await Task.WhenAll(playlistsTask, albumsTask, tracksTask);

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
            gotFirstPage = await tracksTask;
            if (gotFirstPage)
                AddLikedTracks(likedPages.Current);
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

        try
        {
            while (gotFirstPage && await likedPages.MoveNextAsync())
                _player.OnPageLoaded(AddLikedTracks(likedPages.Current));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Keep the songs that did arrive.
            System.Diagnostics.Debug.WriteLine($"LibraryPage: loading more liked songs failed: {ex}");
        }
        finally
        {
            _player.Complete();
            await likedPages.DisposeAsync();
        }
    }

    private List<Track> AddLikedTracks(IEnumerable<TidalTrackItem> items)
    {
        var added = new List<Track>();
        foreach (var item in items)
        {
            // Liking a song while the list loads shifts the later pages by one, which would
            // otherwise repeat the song at the page boundary.
            if (item.Item is null || _unlikedTrackIds.Contains(item.Item.Id)
                || !_likedTrackIds.Add(item.Item.Id)) continue;
            var track = MapTrack(item.Item, _likedTracks.Count + 1);
            _likedTracks.Add(track);
            added.Add(track);
        }

        UpdateLikedSongsVisibility();
        return added;
    }

    /// <summary>Keeps the list in step with likes made elsewhere: the playback bar, a context menu.</summary>
    private void OnTrackLikedChanged(Track track, bool liked)
    {
        if (!_loaded) return;

        if (liked)
        {
            _unlikedTrackIds.Remove(track.Id);
            if (!_likedTrackIds.Add(track.Id)) return;

            // Newest first, the order TIDAL lists them in.
            var copy = track.Clone();
            copy.IsLiked = true;
            _likedTracks.Insert(0, copy);
        }
        else
        {
            _unlikedTrackIds.Add(track.Id);
            if (!_likedTrackIds.Remove(track.Id)) return;

            for (var i = 0; i < _likedTracks.Count; i++)
            {
                if (_likedTracks[i].Id != track.Id) continue;
                _likedTracks.RemoveAt(i);
                break;
            }
        }

        for (var i = 0; i < _likedTracks.Count; i++)
            _likedTracks[i].RowNumber = i + 1;
        UpdateLikedSongsVisibility();
    }

    private void UpdateLikedSongsVisibility() =>
        LikedSongsSection.Visibility = _likedTracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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
        if (e.ClickedItem is Track track)
        {
            var idx = _likedTracks.IndexOf(track);
            _player.Play(_likedTracks, idx >= 0 ? idx : 0);
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
            ArtistId = a.Artist?.Id ?? a.Artists.FirstOrDefault()?.Id ?? 0,
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
