using System.Linq;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>Albums, artists and playlists from the API as the cards the pages show.</summary>
public static class CardMapper
{
    public static HomeSectionItem FromAlbum(TidalAlbum album)
    {
        var artist = album.Artist ?? album.Artists.FirstOrDefault();
        return new HomeSectionItem
        {
            Id = album.Id.ToString(),
            NumericId = album.Id,
            Title = album.Title,
            Subtitle = artist?.Name ?? string.Empty,
            ArtistId = artist?.Id ?? 0,
            CoverUrl = TidalApiClient.GetImageUrl(album.Cover, 320, 320),
            IsExplicit = album.Explicit,
            ItemType = "album",
        };
    }

    public static HomeSectionItem FromArtist(TidalArtist artist) => new()
    {
        Id = artist.Id.ToString(),
        NumericId = artist.Id,
        Title = artist.Name,
        CoverUrl = TidalApiClient.GetImageUrl(artist.Picture, 320, 320),
        ItemType = "artist",
    };

    public static HomeSectionItem FromPlaylist(TidalPlaylist playlist) => new()
    {
        Id = playlist.Uuid,
        Title = playlist.Title,
        Subtitle = !string.IsNullOrEmpty(playlist.Creator?.Name)
            ? playlist.Creator!.Name
            : Loc.Format("PlaylistDetail_Tracks", playlist.NumberOfTracks),
        CoverUrl = TidalApiClient.GetImageUrl(playlist.SquareImage ?? playlist.Image, 320, 320),
        ItemType = "playlist",
    };
}
