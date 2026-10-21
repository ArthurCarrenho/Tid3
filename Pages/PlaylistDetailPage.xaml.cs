using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Models;
using TidalUi3.Services;
using TidalUi3.Helpers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TidalUi3.Pages;

public sealed partial class PlaylistDetailPage : Page
{
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private readonly ResourceLoader _resourceLoader = new();
    private readonly BulkObservableCollection<Track> _tracks = [];
    private readonly LoadingTrackListPlayer _player;
    private readonly CancellationTokenSource _loadCts = new();
    private PageLoader? _pageLoader;

    // What the save button saves: an album, or someone else's playlist.
    private int _albumId;
    private string? _savablePlaylistUuid;

    // The user's own playlist, whose tracks can be removed. Removals address an item by its
    // position in the playlist on TIDAL, which isn't the row index: unavailable items (videos,
    // tracks pulled from the catalog) are in the playlist but not shown.
    private string? _ownPlaylistUuid;
    private readonly List<int> _positions = [];
    private int _playlistItemsSeen;
    private bool _isLoadingPages;
    // Edits made from this page; the reload PlaylistService announces for them isn't needed.
    private int _ownEditsInFlight;

    public PlaylistDetailPage()
    {
        InitializeComponent();
        _player = new(_queue);
        TracksList.ItemsSource = _tracks;
        FavoritesService.Changed += UpdateSaveButton;
        PlaylistService.PlaylistEdited += OnPlaylistEdited;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _pageLoader ??= new PageLoader(DetailLoading, OnLoadError);

        switch (e.Parameter)
        {
            case string paramStr when paramStr.StartsWith("radio:"):
                if (int.TryParse(paramStr[6..], out var trackId))
                    LoadTrackRadio(trackId);
                break;
            case string playlistUuid:
                LoadPlaylist(playlistUuid);
                break;
            case int albumId:
                LoadAlbum(albumId);
                break;
            case TrackListRequest request:
                LoadTrackList(request);
                break;
        }
    }

    // Pages aren't cached, so this instance is never shown again: stop fetching the rest
    // of a long playlist.
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _loadCts.Cancel();
        FavoritesService.Changed -= UpdateSaveButton;
        PlaylistService.PlaylistEdited -= OnPlaylistEdited;
    }

    private void OnLoadError(string message) => HeaderTitle.Text = message;

    private async void LoadPlaylist(string uuid)
    {
        var ct = _loadCts.Token;
        IAsyncEnumerator<List<TidalTrackItem>>? pages = null;
        var gotFirstPage = false;

        await _pageLoader!.LoadAsync(async () =>
        {
            if (uuid.Length != 36)
            {
                LoadMix(await _api.GetMixAsync(uuid));
                return;
            }

            var playlist = await _api.GetPlaylistAsync(uuid);
            SetHeader(playlist.Title,
                string.Format(_resourceLoader.GetString("PlaylistDetail_Tracks"), playlist.NumberOfTracks),
                TidalApiClient.GetImageUrl(playlist.SquareImage ?? playlist.Image, 320, 320));

            if (PlaylistService.IsOwn(playlist))
                _ownPlaylistUuid = uuid;
            else
                _savablePlaylistUuid = uuid;
            UpdateSaveButton();

            // Only the first page waits behind the loading overlay. A playlist with thousands
            // of tracks takes dozens of requests, and the rest streams in below while the
            // first tracks can already be seen and played.
            _isLoadingPages = true;
            _player.BeginLoading();
            pages = _api.GetPlaylistTrackPagesAsync(uuid, ct).GetAsyncEnumerator();
            gotFirstPage = await pages.MoveNextAsync();
            if (gotFirstPage)
                AddTracks(pages.Current);
        }, _resourceLoader.GetString("PlaylistDetail_LoadError_Playlist"));

        if (pages is null)
            return;

        try
        {
            while (gotFirstPage && await pages.MoveNextAsync())
                _player.OnPageLoaded(AddTracks(pages.Current));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Keep the tracks that did arrive rather than replacing the page with an error.
            System.Diagnostics.Debug.WriteLine($"PlaylistDetailPage: loading more tracks failed: {ex}");
        }
        finally
        {
            _isLoadingPages = false;
            _player.Complete();
            await pages.DisposeAsync();
        }
    }

    private List<Track> AddTracks(IEnumerable<TidalTrackItem> items)
    {
        var added = new List<Track>();
        foreach (var item in items)
        {
            var position = _playlistItemsSeen++;
            if (item.Item is null) continue;
            var track = TrackMapper.Map(item.Item, _tracks.Count + 1);
            _tracks.Add(track);
            _positions.Add(position);
            added.Add(track);
        }
        return added;
    }

    private List<Track> AddTracks(IEnumerable<TidalTrack> items)
    {
        var added = new List<Track>();
        foreach (var item in items)
        {
            var track = TrackMapper.Map(item, _tracks.Count + 1);
            _tracks.Add(track);
            added.Add(track);
        }
        return added;
    }

    private void LoadMix((TidalPlaylist Info, List<TidalTrackItem> Tracks) mix)
    {
        SetHeader(mix.Info.Title,
            string.IsNullOrEmpty(mix.Info.Description) ? _resourceLoader.GetString("PlaylistDetail_Mix") : mix.Info.Description,
            TidalApiClient.GetImageUrl(mix.Info.SquareImage ?? mix.Info.Image, 320, 320));

        var index = 1;
        _tracks.ReplaceAll(mix.Tracks
            .Where(i => i.Item is not null)
            .Select(i => TrackMapper.Map(i.Item!, index++))
            .ToList());
    }

    private async void LoadAlbum(int albumId)
    {
        _albumId = albumId;
        UpdateSaveButton();

        await _pageLoader!.LoadAsync(async () =>
        {
            var album = await _api.GetAlbumAsync(albumId);
            var subtitle = BuildAlbumSubtitle(album);
            var coverUrl = TidalApiClient.GetImageUrl(album.Cover, 320, 320);

            SetHeader(album.Title, subtitle, coverUrl);
            _tracks.ReplaceAll(await LoadAlbumTracksAsync(album, coverUrl));
        }, _resourceLoader.GetString("PlaylistDetail_LoadError_Album"));
    }

    private static string BuildAlbumSubtitle(TidalAlbum album)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(album.Artist?.Name))
            parts.Add(album.Artist.Name);
        if (album.NumberOfTracks > 0)
        {
            var loader = new ResourceLoader();
            parts.Add(string.Format(loader.GetString("PlaylistDetail_Tracks"), album.NumberOfTracks));
        }
        if (!string.IsNullOrEmpty(album.ReleaseDate) && album.ReleaseDate.Length >= 4)
            parts.Add(album.ReleaseDate[..4]);
        if (!string.IsNullOrEmpty(album.AudioQuality))
            parts.Add(QualityHelper.FormatQuality(album));

        return parts.Count > 0 ? string.Join(" • ", parts) : new ResourceLoader().GetString("PlaylistDetail_UnknownAlbum");
    }

    private async Task<List<Track>> LoadAlbumTracksAsync(TidalAlbum album, string? coverUrl)
    {
        var result = await _api.GetAlbumTracksAsync(albumId: album.Id);
        var index = 1;
        return result.Items
            .Select(t =>
            {
                var track = TrackMapper.Map(t, index++);
                track.CoverUrl = coverUrl ?? track.CoverUrl;
                track.Album = album.Title;
                return track;
            })
            .ToList();
    }

    private async void LoadTrackRadio(int trackId)
    {
        await _pageLoader!.LoadAsync(async () =>
        {
            var track = await _api.GetTrackAsync(trackId);
            var coverUrl = TidalApiClient.GetImageUrl(track.Album?.Cover, 320, 320);

            SetHeader(string.Format(_resourceLoader.GetString("PlaylistDetail_RadioTitle"), track.Title),
                track.Artist?.Name ?? _resourceLoader.GetString("PlaylistDetail_VariousArtists"),
                coverUrl);

            _tracks.ReplaceAll(await LoadRadioTracksAsync(trackId));
        }, _resourceLoader.GetString("PlaylistDetail_LoadError_Radio"));
    }

    private async Task<List<Track>> LoadRadioTracksAsync(int trackId)
    {
        var result = await _api.GetTrackRadioAsync(trackId, limit: 50);
        var index = 1;
        return result.Items
            .Where(t => t != null)
            .Select(t => TrackMapper.Map(t, index++))
            .ToList();
    }

    /// <summary>A list from search or a page module ("see all"), paged in like a long playlist.</summary>
    private async void LoadTrackList(TrackListRequest request)
    {
        var ct = _loadCts.Token;
        var hasMore = false;

        await _pageLoader!.LoadAsync(async () =>
        {
            SetHeader(request.Title, request.Subtitle, null);
            _player.BeginLoading();
            var first = await request.LoadPageAsync(0, ct);
            AddTracks(first.Items);
            hasMore = first.HasMore && first.Items.Count > 0;
        }, _resourceLoader.GetString("PlaylistDetail_LoadError_Playlist"));

        try
        {
            while (hasMore && _tracks.Count < request.MaxTracks)
            {
                var page = await request.LoadPageAsync(_tracks.Count, ct);
                _player.OnPageLoaded(AddTracks(page.Items));
                hasMore = page.HasMore && page.Items.Count > 0;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlaylistDetailPage: loading more tracks failed: {ex}");
        }
        finally
        {
            _player.Complete();
            if (string.IsNullOrEmpty(request.Subtitle) && _tracks.Count > 0)
                HeaderSubtitle.Text = string.Format(_resourceLoader.GetString("PlaylistDetail_Tracks"), _tracks.Count);
        }
    }

    private void SetHeader(string title, string subtitle, string? coverUrl)
    {
        HeaderTitle.Text = title;
        HeaderSubtitle.Text = subtitle;
        if (!string.IsNullOrEmpty(coverUrl))
            HeaderCoverControl.ImageSource = coverUrl;
    }

    // ── Saving to the library ──

    private void UpdateSaveButton()
    {
        bool? saved = _albumId > 0 ? FavoritesService.IsAlbumSaved(_albumId)
            : _savablePlaylistUuid is { } uuid ? FavoritesService.IsPlaylistSaved(uuid)
            : null;

        SaveButton.Visibility = saved is null ? Visibility.Collapsed : Visibility.Visible;
        if (saved is not bool isSaved) return;
        SaveButton.Glyph = isSaved ? Glyphs.LikeFilled : Glyphs.Like;
        SaveButton.Text = Loc.Get(isSaved ? "Library_Saved" : "Library_Save");
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var done = _albumId > 0
            ? await FavoritesService.SetAlbumSavedAsync(_albumId, !FavoritesService.IsAlbumSaved(_albumId))
            : _savablePlaylistUuid is { } uuid && await FavoritesService.SetPlaylistSavedAsync(uuid, !FavoritesService.IsPlaylistSaved(uuid));
        if (!done)
            App.MainWindow?.ShowNotice(Loc.Get("Library_SaveFailed"), InfoBarSeverity.Error);
    }

    // ── Editing the user's own playlist ──

    private void AddOwnPlaylistItems(MenuFlyout menu, Track track)
    {
        // Positions shift while pages are still arriving, so removing waits for the full list.
        if (_ownPlaylistUuid is null || _isLoadingPages) return;

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = Loc.Get("Playlist_RemoveTrack"),
            Icon = new FontIcon { Glyph = Glyphs.Remove },
            Command = new RelayCommand(() => RemoveFromPlaylist(track)),
        });
    }

    private async void RemoveFromPlaylist(Track track)
    {
        var row = _tracks.IndexOf(track);
        if (row < 0 || _ownPlaylistUuid is not { } uuid) return;

        _ownEditsInFlight++;
        try
        {
            await PlaylistService.RemoveItemAsync(uuid, _positions[row]);

            _tracks.RemoveAt(row);
            _positions.RemoveAt(row);
            for (var i = row; i < _positions.Count; i++)
                _positions[i]--;
            _playlistItemsSeen--;
            for (var i = 0; i < _tracks.Count; i++)
                _tracks[i].RowNumber = i + 1;
            HeaderSubtitle.Text = string.Format(_resourceLoader.GetString("PlaylistDetail_Tracks"), _playlistItemsSeen);
        }
        catch (Exception ex)
        {
            AppLog.Exception("RemoveFromPlaylistError", ex);
            App.MainWindow?.ShowNotice(Loc.Get("Playlist_RemoveFailed"), InfoBarSeverity.Error);
        }
        finally
        {
            _ownEditsInFlight--;
        }
    }

    // Tracks added to this playlist from elsewhere (a context menu, the playback bar) only show
    // up after a reload.
    private void OnPlaylistEdited(string uuid)
    {
        if (uuid != _ownPlaylistUuid || _ownEditsInFlight > 0 || _isLoadingPages) return;

        _tracks.ReplaceAll([]);
        _positions.Clear();
        _playlistItemsSeen = 0;
        LoadPlaylist(uuid);
    }

    // ── Playback ──

    private void PlayAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count > 0)
            _player.Play(_tracks, 0);
    }

    private void ShufflePlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count > 0)
            _player.PlayShuffled(_tracks);
    }

    private void TrackItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            var idx = _tracks.IndexOf(track);
            _player.Play(_tracks, idx >= 0 ? idx : 0);
        }
    }

    private void TrackItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        => TrackContextMenu.Show(sender, e, _queue, (t, p) => Frame.Navigate(t, p), AddOwnPlaylistItems);
}
