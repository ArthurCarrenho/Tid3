using System.Linq;
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
                ShowForHomeSectionItem(item, source, e, frame);
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

    private static void ShowForHomeSectionItem(HomeSectionItem item, FrameworkElement source, RightTappedRoutedEventArgs e, Frame frame)
    {
        var menu = new MenuFlyout();

        if (item.ItemType == "mix")
        {
            menu.Items.Add(CreateItem(Loc.Get("Menu_OpenMix"), Glyphs.Play,
                () => frame.Navigate(typeof(PlaylistDetailPage), item.Id)));
        }
        else if (item.ItemType == "album" && item.NumericId > 0)
        {
            menu.Items.Add(CreateItem(Loc.Get("Menu_OpenAlbum"), Glyphs.Album,
                () => frame.Navigate(typeof(PlaylistDetailPage), item.NumericId)));
            AddSaveAlbum(menu, item.NumericId);

            if (item.ArtistId > 0 && !string.IsNullOrEmpty(item.Subtitle))
                menu.Items.Add(CreateItem(Loc.Format("Menu_GoTo", item.Subtitle), Glyphs.Artist,
                    () => frame.Navigate(typeof(ArtistPage), item.ArtistId)));
        }
        else if (item.ItemType == "playlist" && item.Id.Length > 0)
        {
            menu.Items.Add(CreateItem(Loc.Get("Menu_OpenPlaylist"), Glyphs.Playlist,
                () => frame.Navigate(typeof(PlaylistDetailPage), item.Id)));
            // The user's own playlists are theirs already; there is nothing to save.
            if (!PlaylistService.OwnPlaylists.Any(p => p.Uuid == item.Id))
            {
                var saved = FavoritesService.IsPlaylistSaved(item.Id);
                menu.Items.Add(CreateItem(Loc.Get(saved ? "Menu_RemoveFromLibrary" : "Menu_SaveToLibrary"),
                    saved ? Glyphs.LikeFilled : Glyphs.Like,
                    () => Report(FavoritesService.SetPlaylistSavedAsync(item.Id, !saved))));
            }
        }
        else if (item.ItemType == "artist" && item.NumericId > 0)
        {
            menu.Items.Add(CreateItem(Loc.Get("Menu_GoToArtist"), Glyphs.Artist,
                () => frame.Navigate(typeof(ArtistPage), item.NumericId)));
            var following = FavoritesService.IsArtistFollowed(item.NumericId);
            menu.Items.Add(CreateItem(Loc.Get(following ? "Menu_Unfollow" : "Menu_Follow"),
                following ? Glyphs.LikeFilled : Glyphs.Like,
                () => Report(FavoritesService.SetArtistFollowedAsync(item.NumericId, !following))));
        }

        ShowMenu(menu, source, e);
    }

    private static void AddSaveAlbum(MenuFlyout menu, int albumId)
    {
        var saved = FavoritesService.IsAlbumSaved(albumId);
        menu.Items.Add(CreateItem(Loc.Get(saved ? "Menu_RemoveFromLibrary" : "Menu_SaveToLibrary"),
            saved ? Glyphs.LikeFilled : Glyphs.Like,
            () => Report(FavoritesService.SetAlbumSavedAsync(albumId, !saved))));
    }

    // The collection rolls a failed change back by itself; the user still needs to be told.
    private static async void Report(System.Threading.Tasks.Task<bool> change)
    {
        if (!await change)
            App.MainWindow?.ShowNotice(Loc.Get("Library_SaveFailed"), InfoBarSeverity.Error);
    }

    private static void ShowForAlbum(Album album, FrameworkElement source, RightTappedRoutedEventArgs e, Frame frame)
    {
        var menu = new MenuFlyout();

        if (album.Id > 0)
        {
            menu.Items.Add(CreateItem(Loc.Get("Menu_OpenAlbum"), Glyphs.Album,
                () => frame.Navigate(typeof(PlaylistDetailPage), album.Id)));
            AddSaveAlbum(menu, album.Id);
        }

        if (album.ArtistId > 0 && !string.IsNullOrEmpty(album.Artist))
            menu.Items.Add(CreateItem(Loc.Format("Menu_GoTo", album.Artist), Glyphs.Artist,
                () => frame.Navigate(typeof(ArtistPage), album.ArtistId)));

        ShowMenu(menu, source, e);
    }

    private static MenuFlyoutItem CreateItem(string text, string glyph, System.Action action) => new()
    {
        Text = text,
        Icon = new FontIcon { Glyph = glyph },
        Command = new RelayCommand(action)
    };

    private static void ShowMenu(MenuFlyout menu, FrameworkElement source, RightTappedRoutedEventArgs e)
    {
        if (menu.Items.Count == 0) return;

        menu.ShowAt(source, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Position = e.GetPosition(source)
        });

        // The card sits inside lists that have context menus of their own.
        e.Handled = true;
    }
}
