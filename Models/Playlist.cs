using System.Collections.Generic;

namespace TidalUi3.Models;

public class Playlist
{
    public int Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CoverGlyph { get; set; } = "\uE8D6";
    public string Color { get; set; } = "#4A90D9";
    public string CoverUrl { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public string Duration { get; set; } = string.Empty;
    public List<Track> Tracks { get; set; } = new();
}
