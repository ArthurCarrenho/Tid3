using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Controls;
using TidalUi3.Models;
using TidalUi3.Services;
using TidalUi3.Helpers;

namespace TidalUi3.Pages;

public sealed partial class LibraryPage : Page
{
    // Each row shows this many; "see all" pages through the rest.
    private const int PreviewCount = 20;

    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private readonly ObservableCollection<Track> _likedTracks = [];
    private readonly HashSet<int> _likedTrackIds = [];
    // Unliked while the list was still loading: a later page may still contain them.
    private readonly HashSet<int> _unlikedTrackIds = [];
    private readonly LoadingTrackListPlayer _player;
    private readonly CancellationTokenSource _loadCts = new();
    // Cards as loaded, each with whether it was in the collection then: removing one from the
    // library elsewhere hides it, and a failed removal brings it back.
    private readonly List<(HomeSectionItem Card, bool WasSaved)> _albums = [];
    private readonly List<(HomeSectionItem Card, bool WasSaved)> _artists = [];
    private int _playlistsVersion;
    private bool _loaded;

    public LibraryPage()
    {
        InitializeComponent();
        _player = new(_queue);
        LikedSongsList.ItemsSource = _likedTracks;
        AlbumsCarousel.Title = Loc.Get("LibraryAlbums_Title");
        ArtistsCarousel.Title = Loc.Get("LibraryArtists_Title");
        Loaded += LibraryPage_Loaded;
        FavoritesService.TrackLikedChanged += OnTrackLikedChanged;
        FavoritesService.Changed += ShowSavedCards;
        FavoritesService.LibraryPlaylistsChanged += ReloadPlaylists;
        PlaylistService.Changed += ReloadPlaylists;

        Unloaded += (_, _) =>
        {
            // Pages aren't cached, so once this one is gone nobody sees the rest of the list.
            _loadCts.Cancel();
            FavoritesService.TrackLikedChanged -= OnTrackLikedChanged;
            FavoritesService.Changed -= ShowSavedCards;
            FavoritesService.LibraryPlaylistsChanged -= ReloadPlaylists;
            PlaylistService.Changed -= ReloadPlaylists;
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
            var playlistsTask = _api.GetLibraryPlaylistsAsync(limit: PreviewCount, ct: _loadCts.Token);
            var albumsTask = _api.GetFavoriteAlbumsAsync(limit: PreviewCount, ct: _loadCts.Token);
            var artistsTask = _api.GetFavoriteArtistsAsync(limit: PreviewCount, ct: _loadCts.Token);
            var tracksTask = likedPages.MoveNextAsync().AsTask();

            await Task.WhenAll(playlistsTask, albumsTask, artistsTask, tracksTask);

            ShowPlaylists(await playlistsTask);

            var albums = await albumsTask;
            foreach (var album in albums.Items)
                if (album.Item is not null)
                    _albums.Add((CardMapper.FromAlbum(album.Item), FavoritesService.IsAlbumSaved(album.Item.Id)));
            AlbumsCarousel.IsShowAllVisible = albums.TotalNumberOfItems > albums.Items.Count;

            var artists = await artistsTask;
            foreach (var artist in artists.Items)
                if (artist.Item is not null)
                    _artists.Add((CardMapper.FromArtist(artist.Item), FavoritesService.IsArtistFollowed(artist.Item.Id)));
            ArtistsCarousel.IsShowAllVisible = artists.TotalNumberOfItems > artists.Items.Count;
            ShowSavedCards();

            // Liked Tracks
            gotFirstPage = await tracksTask;
            if (gotFirstPage)
                AddLikedTracks(likedPages.Current);
        }
        catch
        {
            PlaylistsSection.Visibility = Visibility.Collapsed;
            AlbumsCarousel.Visibility = Visibility.Collapsed;
            ArtistsCarousel.Visibility = Visibility.Collapsed;
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

    private void ShowPlaylists(TidalPagedResult<TidalPlaylistEntry> page)
    {
        var playlists = page.Items
            .Where(e => e.Playlist is not null)
            .Select(e => MapPlaylist(e.Playlist!))
            .ToList();
        UserPlaylistsGrid.ItemsSource = new ObservableCollection<Playlist>(playlists);
        PlaylistsSection.Visibility = playlists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistsShowAll.Visibility = page.TotalNumberOfItems > page.Items.Count ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A playlist was created, saved or removed: the first page may have changed.</summary>
    private async void ReloadPlaylists()
    {
        if (!_loaded) return;
        var version = ++_playlistsVersion;
        try
        {
            var page = await _api.GetLibraryPlaylistsAsync(limit: PreviewCount, ct: _loadCts.Token);
            if (version == _playlistsVersion)
                ShowPlaylists(page);
        }
        catch (Exception ex)
        {
            // The list shown is only out of date, which the next visit fixes.
            System.Diagnostics.Debug.WriteLine($"LibraryPage: reloading playlists failed: {ex.Message}");
        }
    }

    private void ShowSavedCards()
    {
        Show(AlbumsCarousel, _albums, FavoritesService.IsAlbumSaved);
        Show(ArtistsCarousel, _artists, FavoritesService.IsArtistFollowed);

        static void Show(CarouselSectionControl carousel, List<(HomeSectionItem Card, bool WasSaved)> cards, Func<int, bool> isSaved)
        {
            var shown = cards.Where(c => !c.WasSaved || isSaved(c.Card.NumericId)).Select(c => c.Card).ToList();
            carousel.ItemsSource = new ObservableCollection<HomeSectionItem>(shown);
            carousel.Visibility = shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ─── Click Handlers ────────────────────────────────────────

    private void PlaylistItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist playlist && !string.IsNullOrEmpty(playlist.Uuid))
            Frame.Navigate(typeof(PlaylistDetailPage), playlist.Uuid);
    }

    private void CardItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeSectionItem item)
            ItemNavigator.Open(Frame, item);
    }

    private void PlaylistsShowAll_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ItemsGridPage), new ItemsGridRequest(PlaylistsTitle.Text,
            PagedLoaders.Create((limit, offset, ct) => _api.GetLibraryPlaylistsAsync(limit, offset, ct),
                (TidalPlaylistEntry entry) => entry.Playlist is { } p ? CardMapper.FromPlaylist(p) : null)));

    private void AlbumsShowAll_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ItemsGridPage), new ItemsGridRequest(AlbumsCarousel.Title,
            PagedLoaders.Create((limit, offset, ct) => _api.GetFavoriteAlbumsAsync(limit, offset, ct),
                (TidalAlbumItem entry) => entry.Item is { } a ? CardMapper.FromAlbum(a) : null)));

    private void ArtistsShowAll_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ItemsGridPage), new ItemsGridRequest(ArtistsCarousel.Title,
            PagedLoaders.Create((limit, offset, ct) => _api.GetFavoriteArtistsAsync(limit, offset, ct),
                (TidalArtistItem entry) => entry.Item is { } a ? CardMapper.FromArtist(a) : null)));

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

    private static Playlist MapPlaylist(TidalPlaylist p) => new()
    {
        Uuid = p.Uuid,
        Title = p.Title,
        Description = p.Description,
        TrackCount = p.NumberOfTracks,
        TrackCountText = Loc.Format("PlaylistDetail_Tracks", p.NumberOfTracks),
        Duration = FormatHelper.FormatPlaylistDuration(p.Duration),
        CoverUrl = TidalApiClient.GetImageUrl(p.SquareImage ?? p.Image, 320, 320)
    };

}
