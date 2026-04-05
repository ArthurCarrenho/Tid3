using System.Collections.Generic;

namespace TidalUi3.Models;

public class Album
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public string CoverGlyph { get; set; } = "\uE8D6";
    public string Color { get; set; } = "#1DB954";
    public string CoverUrl { get; set; } = string.Empty;
    public List<Track> Tracks { get; set; } = new();
}
