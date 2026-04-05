using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TidalUi3.Models;
using TidalUi3.Services;
using TidalUi3.Helpers;

namespace TidalUi3.Pages;

public sealed partial class SearchPage : Page
{
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private readonly DispatcherTimer _debounceTimer;
    private List<Track>? _searchResults;

    public SearchPage()
    {
        InitializeComponent();
        LoadGenres();

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    // ─── Genre Chips ───────────────────────────────────────────

    private void LoadGenres()
    {
        var genres = new List<string> {
            "Pop", "Rock", "Hip-Hop", "R&B", "Electronic",
            "Jazz", "Classical", "Country", "Latin", "Indie",
            "Metal", "Folk", "Blues", "Reggae", "Punk",
            "Soul", "Funk", "Ambient", "Lo-Fi", "K-Pop"
        };
        var colors = new List<string> {
            "#E53935", "#1E88E5", "#8E24AA", "#43A047", "#FB8C00",
            "#5C6BC0", "#D81B60", "#00897B", "#F4511E", "#3949AB",
            "#6D4C41", "#00ACC1", "#7CB342", "#C0CA33", "#039BE5",
            "#EC407A", "#AB47BC", "#26A69A", "#9CCC65", "#FF7043"
        };

        for (int i = 0; i < genres.Count; i++)
        {
            var color = ParseColor(colors[i]);
            var grid = new Grid
            {
                Width = 190,
                Height = 100,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Tag = genres[i],
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = color, Offset = 0 },
                        new GradientStop { Color = Colors.Black, Offset = 1.2 }
                    }
                }
            };
            grid.Children.Add(new TextBlock
            {
                Text = genres[i],
                Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Bottom
            });
            GenresGrid.Items.Add(grid);
        }
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return Windows.UI.Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    // ─── Search Logic ──────────────────────────────────────────

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Always suppress the built-in suggestion popup
        sender.IsSuggestionListOpen = false;

        _debounceTimer.Stop();

        var query = sender.Text?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            SearchResultsPanel.Visibility = Visibility.Collapsed;
            BrowsePanel.Visibility = Visibility.Visible;
            return;
        }

        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;

        _debounceTimer.Start();
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        sender.IsSuggestionListOpen = false;
        _debounceTimer.Stop();
        DebounceTimer_Tick(null, null!);
    }

    private async void DebounceTimer_Tick(object? sender, object e)
    {
        _debounceTimer.Stop();

        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query) || !_api.IsAuthenticated)
            return;

        SearchLoading.IsLoading = true;
        SearchResultsPanel.Visibility = Visibility.Visible;
        BrowsePanel.Visibility = Visibility.Collapsed;

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
        }
        catch
        {
            ArtistsSection.Visibility = Visibility.Collapsed;
            AlbumsSection.Visibility = Visibility.Collapsed;
            SongsSection.Visibility = Visibility.Collapsed;
        }
        finally
        {
            SearchLoading.IsLoading = false;
        }
    }

    // ─── Click Handlers ────────────────────────────────────────

    private void GenreItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Grid grid && grid.Tag is string genre)
            SearchBox.Text = genre;
    }

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
