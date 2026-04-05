using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Windows.Input;
using TidalUi3.Models;
using TidalUi3.Pages;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>
/// Shared right-click context menus for track and search-suggestion items across all pages.
/// </summary>
public static class TrackContextMenu
{
    public static void Show(object sender, RightTappedRoutedEventArgs e, QueueService queue, Action<Type, object> navigate)
    {
        // Walk up visual tree to find Track DataContext
        Track? track = null;
        var source = e.OriginalSource as FrameworkElement;
        while (source != null)
        {
            if (source.DataContext is Track t) { track = t; break; }
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source) as FrameworkElement;
        }

        // Fallback: sender itself
        if (track == null && sender is FrameworkElement senderEl && senderEl.DataContext is Track senderTrack)
        {
            track = senderTrack;
            source = senderEl;
        }

        if (track == null || source == null) return;

        var menu = new MenuFlyout();
        PopulateForTrack(menu, track, queue, navigate);
        menu.ShowAt(source, new FlyoutShowOptions { Position = e.GetPosition(source) });
    }

    /// <summary>
    /// Populates a MenuFlyout with context actions for a SearchSuggestion.
    /// </summary>
    public static void PopulateForSuggestion(MenuFlyout menu, SearchSuggestion s, QueueService queue, Action<Type, object> navigate)
    {
        menu.Items.Clear();

        switch (s.Type)
        {
            case "track":
                // Convert to a Track model and let the dedicated track method handle the rest
                var track = new Track
                {
                    Id = s.TrackId,
                    Title = s.Name,
                    Artist = s.Subtitle?.Replace("Song · ", "") ?? "",
                    CoverUrl = s.CoverUrl,
                    IsExplicit = s.IsExplicit,
                    AlbumId = s.Id,         // We mapped Album ID to the base Id property
                    ArtistId = s.ArtistId   // From the newly added property
                };
                PopulateForTrack(menu, track, queue, navigate);
                break;

            case "album":
                menu.Items.Add(CreateMenuItem("Go to album", Glyphs.Album, new RelayCommand(() => navigate(typeof(PlaylistDetailPage), s.Id))));
                break;

            case "artist":
                menu.Items.Add(CreateMenuItem("Go to artist", Glyphs.Artist, new RelayCommand(() => navigate(typeof(ArtistPage), s.Id))));
                break;
        }
    }

    /// <summary>
    /// Populates a MenuFlyout with context actions for a standard Track.
    /// </summary>
    private static void PopulateForTrack(MenuFlyout menu, Track track, QueueService queue, Action<Type, object> navigate)
    {
        // Playback Actions
        menu.Items.Add(CreateMenuItem("Play", Glyphs.Play, new RelayCommand(() => queue.PlayTrack(track))));
        menu.Items.Add(CreateMenuItem("Play next", Glyphs.Next, new RelayCommand(() => queue.PlayNext(track))));
        menu.Items.Add(CreateMenuItem("Add to queue", Glyphs.Queue, new RelayCommand(() => queue.AddToQueue(track))));

        menu.Items.Add(new MenuFlyoutSeparator());

        // Navigation Actions
        if (track.AlbumId > 0)
            menu.Items.Add(CreateMenuItem("Go to album", Glyphs.Album, new RelayCommand(() => navigate(typeof(PlaylistDetailPage), track.AlbumId))));

        if (track.ArtistId > 0)
            menu.Items.Add(CreateMenuItem("Go to artist", Glyphs.Artist, new RelayCommand(() => navigate(typeof(ArtistPage), track.ArtistId))));

        if (track.Id > 0)
            menu.Items.Add(CreateMenuItem("Go to track radio", Glyphs.Radio, new RelayCommand(() => navigate(typeof(PlaylistDetailPage), $"radio:{track.Id}"))));
    }

    /// <summary>
    /// Helper method to keep menu item creation clean and consistent.
    /// </summary>
    private static MenuFlyoutItem CreateMenuItem(string text, string glyph, ICommand command)
    {
        return new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
            Command = command
        };
    }
}
