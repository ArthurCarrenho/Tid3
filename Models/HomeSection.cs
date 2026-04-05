using System.Collections.Generic;

namespace TidalUi3.Models;

public class HomeSectionItem
{
    public string Id { get; set; } = string.Empty;
    public int NumericId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty; // "mix", "album", "track", "artist"
    public int ArtistId { get; set; }
    public bool IsExplicit { get; set; }
}

public class HomeSection
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "MIX_LIST", "ALBUM_LIST", "TRACKS"
    public List<HomeSectionItem> Items { get; set; } = new();
}
