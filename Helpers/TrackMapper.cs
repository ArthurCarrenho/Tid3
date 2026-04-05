using System.Linq;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>
/// Centralized mapper from TidalTrack API model to the Track view model.
/// Ensures all fields (AlbumId, ArtistId, QualityBadge, etc.) are always populated consistently.
/// </summary>
public static class TrackMapper
{
    public static Track Map(TidalTrack t, int index) => new()
    {
        Id = t.Id,
        RowNumber = index,
        Title = t.Title,
        Artist = t.Artist?.Name ?? t.Artists.FirstOrDefault()?.Name ?? "Unknown",
        Album = t.Album?.Title ?? "",
        AlbumId = t.Album?.Id ?? 0,
        ArtistId = t.Artist?.Id ?? t.Artists.FirstOrDefault()?.Id ?? 0,
        Duration = FormatHelper.FormatDuration(t.Duration),
        DurationSeconds = t.Duration,
        CoverUrl = TidalApiClient.GetImageUrl(t.Album?.Cover, 160, 160),
        AudioQuality = t.AudioQuality,
        QualityBadge = QualityHelper.FormatQuality(t),
        IsExplicit = t.Explicit
    };
}
