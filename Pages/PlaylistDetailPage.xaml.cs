using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private List<Track>? _tracks;
    private PageLoader? _pageLoader;

    public PlaylistDetailPage()
    {
        InitializeComponent();
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
        }
    }

    private void OnLoadError(string message) => HeaderTitle.Text = message;

    private async void LoadPlaylist(string uuid)
    {
        await _pageLoader!.LoadAsync(async () =>
        {
            var isMix = uuid.Length != 36;
            var playlist = isMix
                ? await _api.GetMixAsPlaylistAsync(uuid)
                : await _api.GetPlaylistAsync(uuid);

            SetHeader(playlist.Title,
                isMix ? (playlist.Description ?? _resourceLoader.GetString("PlaylistDetail_Mix")) : string.Format(_resourceLoader.GetString("PlaylistDetail_Tracks"), playlist.NumberOfTracks),
                TidalApiClient.GetImageUrl(playlist.SquareImage ?? playlist.Image, 320, 320));

            _tracks = isMix
                ? await LoadMixTracksAsync(uuid)
                : await LoadPlaylistTracksAsync(uuid);

            TracksList.ItemsSource = new ObservableCollection<Track>(_tracks);
        }, _resourceLoader.GetString("PlaylistDetail_LoadError_Playlist"));
    }

    private async Task<List<Track>> LoadMixTracksAsync(string uuid)
    {
        var result = await _api.GetMixTracksAsync(uuid, limit: 50);
        var index = 1;
        return result.Items
            .Where(i => i.Item is not null)
            .Select(i => TrackMapper.Map(i.Item!, index++))
            .ToList();
    }

    private async Task<List<Track>> LoadPlaylistTracksAsync(string uuid)
    {
        var result = await _api.GetPlaylistTracksAsync(uuid, limit: 100);
        var index = 1;
        return result.Items
            .Where(i => i.Item is not null)
            .Select(i => TrackMapper.Map(i.Item!, index++))
            .ToList();
    }

    private async void LoadAlbum(int albumId)
    {
        await _pageLoader!.LoadAsync(async () =>
        {
            var album = await _api.GetAlbumAsync(albumId);
            var subtitle = BuildAlbumSubtitle(album);
            var coverUrl = TidalApiClient.GetImageUrl(album.Cover, 320, 320);

            SetHeader(album.Title, subtitle, coverUrl);
            _tracks = await LoadAlbumTracksAsync(album, coverUrl);

            TracksList.ItemsSource = new ObservableCollection<Track>(_tracks);
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

            _tracks = await LoadRadioTracksAsync(trackId);
            TracksList.ItemsSource = new ObservableCollection<Track>(_tracks);
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

    private void SetHeader(string title, string subtitle, string? coverUrl)
    {
        HeaderTitle.Text = title;
        HeaderSubtitle.Text = subtitle;
        if (!string.IsNullOrEmpty(coverUrl))
            HeaderCoverControl.ImageSource = coverUrl;
    }

    private void PlayAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks is { Count: > 0 })
            _queue.PlayTracks(_tracks, 0);
    }

    private void ShufflePlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tracks is { Count: > 0 })
        {
            _queue.Shuffle = true;
            _queue.PlayTracks(_tracks, 0);
        }
    }

    private void TrackItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track && _tracks is not null)
        {
            var idx = _tracks.IndexOf(track);
            _queue.PlayTracks(_tracks, idx >= 0 ? idx : 0);
        }
    }

    private void TrackItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        => TrackContextMenu.Show(sender, e, _queue, (t, p) => Frame.Navigate(t, p));
}
