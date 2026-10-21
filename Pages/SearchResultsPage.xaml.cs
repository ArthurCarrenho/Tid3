using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
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
using Microsoft.Windows.ApplicationModel.Resources;

namespace TidalUi3.Pages;

public sealed partial class SearchResultsPage : Page
{
    // Results per type on this page; "see all" pages through the rest.
    private const int PreviewCount = 12;

    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private readonly ResourceLoader _resourceLoader = new();
    private List<Track>? _searchResults;
    private string _query = string.Empty;

    public SearchResultsPage()
    {
        InitializeComponent();
        ArtistsCarousel.Title = _resourceLoader.GetString("SearchHeader_Artists");
        AlbumsCarousel.Title = _resourceLoader.GetString("SearchHeader_Albums");
        PlaylistsCarousel.Title = _resourceLoader.GetString("SearchHeader_Playlists");
        SongsTitle.Text = _resourceLoader.GetString("SearchHeader_Songs");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
            PerformSearch(query);
    }

    private async void PerformSearch(string query)
    {
        _query = query;
        PageHeader.Text = string.Format(_resourceLoader.GetString("SearchResults_Title"), query);
        SearchLoading.IsLoading = true;

        try
        {
            var results = await _api.SearchAsync(query, limit: PreviewCount, types: "TRACKS,ALBUMS,ARTISTS,PLAYLISTS");

            var any = ShowCards(ArtistsCarousel, results.Artists, CardMapper.FromArtist);
            any |= ShowCards(AlbumsCarousel, results.Albums, CardMapper.FromAlbum);
            any |= ShowCards(PlaylistsCarousel, results.Playlists, CardMapper.FromPlaylist);

            // Tracks
            if (results.Tracks?.Items is { Count: > 0 } tracks)
            {
                var index = 1;
                _searchResults = tracks.Select(t => TrackMapper.Map(t, index++)).ToList();
                SearchResultsList.ItemsSource = new ObservableCollection<Track>(_searchResults);
                SongsSection.Visibility = Visibility.Visible;
                SongsShowAll.Visibility = results.Tracks.TotalNumberOfItems > tracks.Count ? Visibility.Visible : Visibility.Collapsed;
                any = true;
            }
            else
            {
                SongsSection.Visibility = Visibility.Collapsed;
                _searchResults = null;
            }

            if (!any)
                PageHeader.Text = string.Format(_resourceLoader.GetString("SearchResults_None"), query);
        }
        catch
        {
            PageHeader.Text = _resourceLoader.GetString("SearchResults_Failed");
        }
        finally
        {
            SearchLoading.IsLoading = false;
        }
    }

    private static bool ShowCards<T>(CarouselSectionControl carousel, TidalPagedResult<T>? results, Func<T, HomeSectionItem> map)
    {
        if (results?.Items is not { Count: > 0 } items)
        {
            carousel.Visibility = Visibility.Collapsed;
            return false;
        }

        carousel.ItemsSource = new ObservableCollection<HomeSectionItem>(items.Select(map));
        carousel.IsShowAllVisible = results.TotalNumberOfItems > items.Count;
        carousel.Visibility = Visibility.Visible;
        return true;
    }

    // ─── See all ───────────────────────────────────────────────

    private string MoreTitle(string section) =>
        string.Format(_resourceLoader.GetString("SearchMore_Title"), section, _query);

    // One type per request, so each page of results is a full page of that type.
    private Func<int, int, CancellationToken, Task<TidalPagedResult<T>>> Search<T>(
        string type, Func<TidalSearchResult, TidalPagedResult<T>?> pick)
    {
        var query = _query;
        return async (limit, offset, ct) => pick(await _api.SearchAsync(query, limit, offset, type, ct)) ?? new();
    }

    private void ArtistsShowAll_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ItemsGridPage), new ItemsGridRequest(MoreTitle(ArtistsCarousel.Title),
            PagedLoaders.Create(Search("ARTISTS", r => r.Artists), (TidalArtist a) => CardMapper.FromArtist(a))));

    private void AlbumsShowAll_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ItemsGridPage), new ItemsGridRequest(MoreTitle(AlbumsCarousel.Title),
            PagedLoaders.Create(Search("ALBUMS", r => r.Albums), (TidalAlbum a) => CardMapper.FromAlbum(a))));

    private void PlaylistsShowAll_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ItemsGridPage), new ItemsGridRequest(MoreTitle(PlaylistsCarousel.Title),
            PagedLoaders.Create(Search("PLAYLISTS", r => r.Playlists), (TidalPlaylist p) => CardMapper.FromPlaylist(p))));

    private void SongsShowAll_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(PlaylistDetailPage), new TrackListRequest(SongsTitle.Text, _query,
            PagedLoaders.Create(Search("TRACKS", r => r.Tracks), (TidalTrack t) => t)));

    // ─── Click Handlers ────────────────────────────────────────

    private void CardItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeSectionItem item)
            ItemNavigator.Open(Frame, item);
    }

    private void TrackItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track && _searchResults is not null)
        {
            var idx = _searchResults.IndexOf(track);
            _queue.PlayTracks(_searchResults, idx >= 0 ? idx : 0);
        }
    }

    private void TrackItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        => TrackContextMenu.Show(sender, e, _queue, (t, p) => Frame.Navigate(t, p));
}
