using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TidalUi3.Models;
using TidalUi3.Pages;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>
/// Shared right-click context menu for media card items (albums, mixes, playlists).
/// Works with HomeSectionItem and Album data contexts.
/// </summary>
public static class MediaCardContextMenu
{
    public static void Show(object sender, RightTappedRoutedEventArgs e, Frame frame)
    {
        var source = e.OriginalSource as FrameworkElement;
        while (source != null)
        {
            if (source.DataContext is HomeSectionItem item)
            {
                ShowForHomeSectionItem(item, source, e);
                return;
            }
            if (source.DataContext is Album album)
            {
                ShowForAlbum(album, source, e, frame);
                return;
            }
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source) as FrameworkElement;
        }
    }

    private static void ShowForHomeSectionItem(HomeSectionItem item, FrameworkElement source, RightTappedRoutedEventArgs e)
    {
        var frame = FindFrame(source);
        if (frame == null) return;

        var menu = new MenuFlyout();

        if (item.ItemType == "mix")
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "Open mix",
                Icon = new FontIcon { Glyph = Glyphs.Play },
                Command = new RelayCommand(() => frame.Navigate(typeof(PlaylistDetailPage), item.Id))
            });
        }
        else if (item.ItemType == "album")
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "Open album",
                Icon = new FontIcon { Glyph = Glyphs.Album },
                Command = new RelayCommand(() =>
                {
                    if (item.NumericId > 0)
                        frame.Navigate(typeof(PlaylistDetailPage), item.NumericId);
                })
            });
            if (item.ArtistId > 0 && !string.IsNullOrEmpty(item.Subtitle))
            {
                menu.Items.Add(new MenuFlyoutItem
                {
                    Text = $"Go to {item.Subtitle}",
                    Icon = new FontIcon { Glyph = Glyphs.Artist },
                    Command = new RelayCommand(() => frame.Navigate(typeof(ArtistPage), item.ArtistId))
                });
            }
        }

        if (menu.Items.Count > 0)
        {
            menu.ShowAt(source, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Position = e.GetPosition(source)
            });
        }
    }

    private static void ShowForAlbum(Album album, FrameworkElement source, RightTappedRoutedEventArgs e, Frame frame)
    {
        var menu = new MenuFlyout();

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Open album",
            Icon = new FontIcon { Glyph = Glyphs.Album },
            Command = new RelayCommand(() =>
            {
                if (album.Id > 0)
                    frame.Navigate(typeof(PlaylistDetailPage), album.Id);
            })
        });

        if (!string.IsNullOrEmpty(album.Artist))
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = $"Go to {album.Artist}",
                Icon = new FontIcon { Glyph = Glyphs.Artist },
                Command = new RelayCommand(() =>
                {
                    // Album model doesn't have ArtistId — navigate by name not possible
                    // This is a placeholder for future enhancement
                })
            });
        }

        menu.ShowAt(source, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Position = e.GetPosition(source)
        });
    }

    private static Frame? FindFrame(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is Page page)
                return page.Frame;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
