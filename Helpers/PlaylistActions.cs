using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>"Add to playlist" with its feedback: the menu, the new-playlist dialog and the notices.</summary>
public static class PlaylistActions
{
    /// <summary>
    /// The "Add to playlist" submenu: "New playlist…" first, then the user's own playlists.
    /// </summary>
    public static MenuFlyoutSubItem CreateAddToPlaylistMenu(IReadOnlyList<Track> tracks)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = Loc.Get("Menu_AddToPlaylist"),
            Icon = new FontIcon { Glyph = Glyphs.AddToPlaylist },
        };

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = Loc.Get("Menu_NewPlaylist"),
            Icon = new FontIcon { Glyph = Glyphs.Add },
            Command = new RelayCommand(() => _ = AddToNewPlaylistAsync(tracks)),
        });

        if (PlaylistService.OwnPlaylists.Count > 0)
            menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var playlist in PlaylistService.OwnPlaylists)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = playlist.Title,
                Command = new RelayCommand(() => _ = AddToPlaylistAsync(playlist, tracks)),
            });
        }
        return menu;
    }

    public static async Task AddToPlaylistAsync(TidalPlaylist playlist, IReadOnlyList<Track> tracks)
    {
        try
        {
            await PlaylistService.AddTracksAsync(playlist.Uuid, tracks.Select(t => t.Id));
            Notify(Loc.Format("Playlist_Added", playlist.Title), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppLog.Exception("AddToPlaylistError", ex);
            Notify(Loc.Format("Playlist_AddFailed", playlist.Title), InfoBarSeverity.Error);
        }
    }

    public static async Task AddToNewPlaylistAsync(IReadOnlyList<Track> tracks)
    {
        var name = await PromptForNameAsync();
        if (name is null) return;

        TidalPlaylist playlist;
        try
        {
            playlist = await PlaylistService.CreateAsync(name);
        }
        catch (Exception ex)
        {
            AppLog.Exception("CreatePlaylistError", ex);
            Notify(Loc.Get("Playlist_CreateFailed"), InfoBarSeverity.Error);
            return;
        }
        await AddToPlaylistAsync(playlist, tracks);
    }

    private static async Task<string?> PromptForNameAsync()
    {
        if (App.MainWindow?.Content is not FrameworkElement root) return null;

        var box = new TextBox { PlaceholderText = Loc.Get("Playlist_NamePlaceholder"), MaxLength = 100 };
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Playlist_NewTitle"),
            Content = box,
            PrimaryButtonText = Loc.Get("Playlist_Create"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = root.XamlRoot,
            RequestedTheme = root.RequestedTheme,
        };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = box.Text.Trim().Length > 0;

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
        }
        catch (Exception ex)
        {
            // Only one dialog can be open at a time.
            AppLog.Exception("PlaylistNameDialogError", ex);
            return null;
        }
    }

    private static void Notify(string message, InfoBarSeverity severity) =>
        App.MainWindow?.ShowNotice(message, severity);
}
