using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TidalUi3.Controls;
using TidalUi3.Models;
using TidalUi3.Pages;

namespace TidalUi3.Helpers;

/// <summary>
/// Turns parsed page sections into UI: rows of cards, link tiles and short track lists, each
/// with its "see all". Home, Explore and the genre pages all render through here.
/// </summary>
public static class PageSectionsBuilder
{
    // Enough to get a feel for the list; "see all" has the rest.
    private const int TrackPreviewCount = 8;

    // Link tiles take a color from their title, so a genre keeps its color between visits.
    private static readonly Windows.UI.Color[] TileColors =
    [
        Microsoft.UI.ColorHelper.FromArgb(255, 0xE5, 0x39, 0x35), Microsoft.UI.ColorHelper.FromArgb(255, 0x1E, 0x88, 0xE5),
        Microsoft.UI.ColorHelper.FromArgb(255, 0x8E, 0x24, 0xAA), Microsoft.UI.ColorHelper.FromArgb(255, 0x43, 0xA0, 0x47),
        Microsoft.UI.ColorHelper.FromArgb(255, 0xFB, 0x8C, 0x00), Microsoft.UI.ColorHelper.FromArgb(255, 0x5C, 0x6B, 0xC0),
        Microsoft.UI.ColorHelper.FromArgb(255, 0xD8, 0x1B, 0x60), Microsoft.UI.ColorHelper.FromArgb(255, 0x00, 0x89, 0x7B),
        Microsoft.UI.ColorHelper.FromArgb(255, 0xF4, 0x51, 0x1E), Microsoft.UI.ColorHelper.FromArgb(255, 0x39, 0x49, 0xAB),
        Microsoft.UI.ColorHelper.FromArgb(255, 0x6D, 0x4C, 0x41), Microsoft.UI.ColorHelper.FromArgb(255, 0x00, 0xAC, 0xC1),
    ];

    /// <summary>
    /// Fetches a TIDAL page and fills <paramref name="host"/> with its sections. Returns the
    /// page's own title (null if it has none), or throws if it couldn't be loaded.
    /// </summary>
    public static async System.Threading.Tasks.Task<string?> LoadAsync(string apiPath, Panel host, Frame frame,
        System.Threading.CancellationToken ct = default)
    {
        var page = await App.ApiClient.GetPageAsync(apiPath, ct);
        host.Children.Clear();
        Fill(host, PageModules.Parse(page), frame);
        return page.TryGetProperty("title", out var title) && title.ValueKind == System.Text.Json.JsonValueKind.String
            && title.GetString() is { Length: > 0 } text ? text : null;
    }

    public static void Fill(Panel host, IEnumerable<PageSection> sections, Frame frame)
    {
        foreach (var section in sections)
        {
            host.Children.Add(section.Kind switch
            {
                PageSectionKind.Links => BuildLinks(section, frame),
                PageSectionKind.Tracks => BuildTracks(section, frame),
                _ => BuildCards(section, frame),
            });
        }
    }

    private static UIElement BuildCards(PageSection section, Frame frame)
    {
        var carousel = new CarouselSectionControl
        {
            Title = section.Title,
            ItemTemplateSelector = (DataTemplateSelector)Application.Current.Resources["SectionItemTemplateSelector"],
            ItemsSource = section.Items,
            IsShowAllVisible = section.ShowMoreApiPath is not null,
        };
        carousel.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is HomeSectionItem item)
                ItemNavigator.Open(frame, item);
        };
        if (section.ShowMoreApiPath is { } more)
            carousel.ShowAllClick += (_, _) => frame.Navigate(typeof(ItemsGridPage),
                new ItemsGridRequest(section.Title, ShowMoreLoaders.Cards(more)));
        return carousel;
    }

    private static UIElement BuildLinks(PageSection section, Frame frame)
    {
        var panel = new StackPanel { Spacing = 12 };
        if (section.Title.Length > 0)
            panel.Children.Add(SectionTitle(section.Title));

        var grid = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
        foreach (var link in section.Items)
            grid.Items.Add(LinkTile(link));
        grid.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: HomeSectionItem link })
                ItemNavigator.Open(frame, link);
        };
        panel.Children.Add(grid);
        return panel;
    }

    private static FrameworkElement LinkTile(HomeSectionItem link)
    {
        var color = TileColors[StableHash(link.Title) % TileColors.Length];
        var tile = new Grid
        {
            Width = 190,
            Height = 88,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            Tag = link,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = color, Offset = 0 },
                    new GradientStop { Color = Colors.Black, Offset = 1.3 },
                },
            },
        };
        tile.Children.Add(new TextBlock
        {
            Text = link.Title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Bottom,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, link.Title);
        return tile;
    }

    private static UIElement BuildTracks(PageSection section, Frame frame)
    {
        var tracks = section.Tracks.Select((t, i) => TrackMapper.Map(t, i + 1)).ToList();

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(SectionTitle(section.Title));
        if (section.ShowMoreApiPath is { } more)
        {
            var seeAll = new HyperlinkButton { Content = Loc.Get("Section_ShowAllText") };
            Grid.SetColumn(seeAll, 1);
            seeAll.Click += (_, _) => frame.Navigate(typeof(PlaylistDetailPage),
                new TrackListRequest(section.Title, string.Empty, ShowMoreLoaders.Tracks(more)));
            header.Children.Add(seeAll);
        }

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            ItemTemplate = (DataTemplate)Application.Current.Resources["TrackRowTemplate"],
            ItemsSource = tracks.Take(TrackPreviewCount).ToList(),
        };
        // Playing from the preview queues the whole module, not just the rows shown.
        list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is Track track)
                App.Queue.PlayTracks(tracks, tracks.IndexOf(track));
        };
        list.RightTapped += (sender, e) => TrackContextMenu.Show(sender, e, App.Queue, (type, parameter) => frame.Navigate(type, parameter));

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(header);
        panel.Children.Add(list);
        return panel;
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    // string.GetHashCode changes between runs; this doesn't.
    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text)
            hash = unchecked(hash * 31 + c);
        return hash & int.MaxValue;
    }
}
