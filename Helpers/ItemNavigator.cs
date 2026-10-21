using Microsoft.UI.Xaml.Controls;
using TidalUi3.Models;
using TidalUi3.Pages;

namespace TidalUi3.Helpers;

/// <summary>Where a card or link leads, so every page opens items the same way.</summary>
public static class ItemNavigator
{
    public static void Open(Frame frame, HomeSectionItem item)
    {
        switch (item.ItemType)
        {
            case "album" when item.NumericId > 0:
                frame.Navigate(typeof(PlaylistDetailPage), item.NumericId);
                break;
            // PlaylistDetailPage tells mixes from playlists by the length of the id.
            case "playlist" or "mix" when item.Id.Length > 0:
                frame.Navigate(typeof(PlaylistDetailPage), item.Id);
                break;
            case "artist" when item.NumericId > 0:
                frame.Navigate(typeof(ArtistPage), item.NumericId);
                break;
            case "link" when item.Id.Length > 0:
                frame.Navigate(typeof(BrowsePage), new BrowseRequest(item.Id, item.Title));
                break;
        }
    }
}
