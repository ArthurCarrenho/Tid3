namespace TidalUi3.Models;

/// <summary>
/// Represents a search suggestion item shown in the header AutoSuggestBox.
/// Two modes: text-only (query suggestions) and rich (with album art).
/// </summary>
public class SearchSuggestion
{
    public string Type { get; set; } = string.Empty;       // "artist", "album", "track", "search", "query"
    public string Display { get; set; } = string.Empty;     // Primary text (title or query)
    public string Subtitle { get; set; } = string.Empty;    // e.g. "Song · blink-182" or "Album · T.S.O.L."
    public string CoverUrl { get; set; } = string.Empty;    // Album/artist art URL
    public string Icon { get; set; } = "\uE721";           // Segoe icon glyph (search icon default)

    public int Id { get; set; }                             // Album or Artist ID
    public int TrackId { get; set; }                        // Track ID
    public int ArtistId { get; set; }                       // NEW: Artist ID for navigation

    public string Name { get; set; } = string.Empty;        // Name for navigation
    public bool IsRichResult { get; set; }                  // true = show cover art row, false = text-only row
    public bool IsExplicit { get; set; }                    // Show [E] badge

    public override string ToString() => Display;
}
