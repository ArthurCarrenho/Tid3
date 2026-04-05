using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TidalUi3.Models;
using TidalUi3.Services;
using TidalUi3.Helpers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TidalUi3.Pages;

public sealed partial class SearchResultsPage : Page
{
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private readonly ResourceLoader _resourceLoader = new();
    private List<Track>? _searchResults;

    public SearchResultsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
            PerformSearch(query);
    }

    private async void PerformSearch(string query)
    {
        PageHeader.Text = string.Format(_resourceLoader.GetString("SearchResults_Title"), query);
        SearchLoading.IsLoading = true;

        try
        {
            var results = await _api.SearchAsync(query, limit: 10, types: "TRACKS,ALBUMS,ARTISTS");

            // Artists
            if (results.Artists?.Items is { Count: > 0 } artists)
            {
                ArtistsGrid.ItemsSource = new ObservableCollection<HomeSectionItem>(
                    artists.Take(6).Select(a => new HomeSectionItem
                    {
                        NumericId = a.Id,
                        Title = a.Name,
                        CoverUrl = TidalApiClient.GetImageUrl(a.Picture, 320, 320),
                        ItemType = "artist"
                    }));
                ArtistsSection.Visibility = Visibility.Visible;
            }
            else
            {
                ArtistsSection.Visibility = Visibility.Collapsed;
            }

            // Albums
            if (results.Albums?.Items is { Count: > 0 } albums)
            {
                AlbumsGrid.ItemsSource = new ObservableCollection<HomeSectionItem>(
                    albums.Take(6).Select(a => new HomeSectionItem
                    {
                        NumericId = a.Id,
                        Title = a.Title,
                        Subtitle = a.Artist?.Name ?? a.Artists.FirstOrDefault()?.Name ?? "Unknown",
                        CoverUrl = TidalApiClient.GetImageUrl(a.Cover, 320, 320),
                        ItemType = "album"
                    }));
                AlbumsSection.Visibility = Visibility.Visible;
            }
            else
            {
                AlbumsSection.Visibility = Visibility.Collapsed;
            }

            // Tracks
            if (results.Tracks?.Items is { Count: > 0 } tracks)
            {
                var index = 1;
                _searchResults = tracks.Select(t => TrackMapper.Map(t, index++)).ToList();
                SearchResultsList.ItemsSource = new ObservableCollection<Track>(_searchResults);
                SongsSection.Visibility = Visibility.Visible;
            }
            else
            {
                SongsSection.Visibility = Visibility.Collapsed;
                _searchResults = null;
            }

            if (ArtistsSection.Visibility == Visibility.Collapsed &&
                AlbumsSection.Visibility == Visibility.Collapsed &&
                SongsSection.Visibility == Visibility.Collapsed)
            {
                PageHeader.Text = string.Format(_resourceLoader.GetString("SearchResults_None"), query);
            }
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

    // ─── Click Handlers ────────────────────────────────────────

    private void ArtistItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeSectionItem item && item.NumericId > 0)
            Frame.Navigate(typeof(ArtistPage), item.NumericId);
    }

    private void AlbumItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeSectionItem item && item.NumericId > 0)
            Frame.Navigate(typeof(PlaylistDetailPage), item.NumericId);
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
