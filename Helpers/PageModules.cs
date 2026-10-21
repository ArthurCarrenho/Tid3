using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

public enum PageSectionKind
{
    /// <summary>Albums, playlists, mixes or artists, shown as a row of cards.</summary>
    Cards,
    /// <summary>Links to other pages (genres, moods, "New", "HiRes"…).</summary>
    Links,
    /// <summary>A list of tracks.</summary>
    Tracks,
}

/// <summary>A module of a TIDAL page that the app knows how to show.</summary>
public sealed class PageSection
{
    public string Title { get; init; } = string.Empty;
    public PageSectionKind Kind { get; init; }

    /// <summary>The module's type as TIDAL names it (ALBUM_LIST, PLAYLIST_LIST…), to parse later pages the same way.</summary>
    public string ModuleType { get; init; } = string.Empty;

    /// <summary>Cards and links. ItemType is "album", "playlist", "mix", "artist" or "link" (apiPath in Id).</summary>
    public List<HomeSectionItem> Items { get; } = [];

    public List<TidalTrack> Tracks { get; } = [];

    /// <summary>The single-module page behind the module's "show more", if it has one.</summary>
    public string? ShowMoreApiPath { get; init; }

    /// <summary>
    /// Entries the module had, including ones that were skipped (videos, unknown kinds). Paging
    /// counts in these, so a skipped entry mustn't shift the next page's offset.
    /// </summary>
    public int RawItemCount { get; set; }
}

/// <summary>A module's full list: its first items, and where to page through the rest.</summary>
public sealed record PageModuleData(PageSection Section, string? DataApiPath, int TotalItems);

/// <summary>
/// Reads TIDAL's page JSON (pages/for_you, pages/explore, genre pages, "show more" pages): rows
/// of modules, each a list of items of one kind. Modules of kinds the app can't show (videos,
/// promotions) are skipped.
/// </summary>
public static class PageModules
{
    public static List<PageSection> Parse(JsonElement page)
    {
        var sections = new List<PageSection>();
        foreach (var module in Modules(page))
            if (ParseModule(module) is { } section)
                sections.Add(section);
        return sections;
    }

    /// <summary>The page a "show more" link opens: one module, paged through its dataApiPath.</summary>
    public static PageModuleData? ParseSingleModulePage(JsonElement page)
    {
        foreach (var module in Modules(page))
        {
            if (ParseModule(module, requireTitle: false) is not { } section) continue;
            var pagedList = module.GetProperty("pagedList");
            var total = pagedList.TryGetProperty("totalNumberOfItems", out var t) && t.TryGetInt32(out var n) ? n : 0;
            return new PageModuleData(section, Text(pagedList, "dataApiPath"), total);
        }
        return null;
    }

    /// <summary>Items of a later page of a module (a pages/data response), read as <paramref name="moduleType"/>.</summary>
    public static PageSection ParseDataPage(JsonElement data, string moduleType)
    {
        var section = new PageSection { ModuleType = moduleType, Kind = KindOf(moduleType) };
        if (data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            AddItems(items, moduleType, section);
        return section;
    }

    private static IEnumerable<JsonElement> Modules(JsonElement page)
    {
        if (!page.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var module in modules.EnumerateArray())
                yield return module;
        }
    }

    private static PageSection? ParseModule(JsonElement module, bool requireTitle = true)
    {
        var type = Text(module, "type") ?? string.Empty;
        if (!module.TryGetProperty("pagedList", out var pagedList)
            || !pagedList.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return null;

        // "Because you listened to …" says more than the module's generic title.
        var title = Text(module, "preTitle") is { Length: > 0 } preTitle ? preTitle : Text(module, "title") ?? string.Empty;
        var kind = KindOf(type);

        // Untitled link rows (Explore's "New", "Top"…) stand on their own; anything else
        // without a title would be an unlabeled row of cards.
        if (requireTitle && title.Length == 0 && kind != PageSectionKind.Links)
            return null;

        var showMore = module.TryGetProperty("showMore", out var more) && more.ValueKind == JsonValueKind.Object
            ? Text(more, "apiPath") : null;

        var section = new PageSection
        {
            Title = title,
            Kind = kind,
            ModuleType = type,
            ShowMoreApiPath = string.IsNullOrEmpty(showMore) ? null : showMore,
        };
        AddItems(items, type, section);
        return section.Items.Count > 0 || section.Tracks.Count > 0 ? section : null;
    }

    private static PageSectionKind KindOf(string moduleType) => moduleType switch
    {
        "TRACK_LIST" => PageSectionKind.Tracks,
        "PAGE_LINKS_CLOUD" or "PAGE_LINKS" => PageSectionKind.Links,
        _ => PageSectionKind.Cards,
    };

    private static void AddItems(JsonElement items, string moduleType, PageSection section)
    {
        foreach (var entry in items.EnumerateArray())
        {
            section.RawItemCount++;
            if (entry.ValueKind != JsonValueKind.Object) continue;

            // Mixed lists wrap each entry as {type, item}; the others hold the item itself.
            var wrapped = entry.TryGetProperty("item", out var inner) && inner.ValueKind == JsonValueKind.Object;
            var item = wrapped ? inner : entry;
            var itemKind = ItemKindOf(moduleType, wrapped ? Text(entry, "type") : null, item);

            if (itemKind == "track")
            {
                if (section.Kind == PageSectionKind.Tracks && Deserialize<TidalTrack>(item) is { } track)
                    section.Tracks.Add(track);
                continue;
            }

            // A track inside a list of cards (mixed lists have them) has nowhere to go.
            if (section.Kind == PageSectionKind.Tracks) continue;
            if (ToCard(item, itemKind) is { } card)
                section.Items.Add(card);
        }
    }

    private static string? ItemKindOf(string moduleType, string? entryType, JsonElement item)
    {
        switch (moduleType)
        {
            case "TRACK_LIST": return "track";
            case "ALBUM_LIST": return "album";
            case "PLAYLIST_LIST": return "playlist";
            case "MIX_LIST": return "mix";
            case "ARTIST_LIST": return "artist";
            case "PAGE_LINKS_CLOUD" or "PAGE_LINKS": return "link";
            case "VIDEO_LIST": return null; // the app doesn't play videos
        }

        switch (entryType)
        {
            case "TRACK": return "track";
            case "ALBUM": return "album";
            case "PLAYLIST": return "playlist";
            case "MIX": return "mix";
            case "ARTIST": return "artist";
            case "VIDEO": return null;
        }

        // Other modules (user mixes, "for you" variants): tell the item apart by its fields.
        if (item.TryGetProperty("apiPath", out _)) return "link";
        if (item.TryGetProperty("uuid", out _)) return "playlist";
        if (item.TryGetProperty("mixType", out _)) return "mix";
        if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) return "mix";
        if (item.TryGetProperty("cover", out _)) return "album";
        if (item.TryGetProperty("picture", out _) && item.TryGetProperty("name", out _)) return "artist";
        return null;
    }

    private static HomeSectionItem? ToCard(JsonElement item, string? kind)
    {
        switch (kind)
        {
            case "album":
            {
                var id = Int(item, "id");
                var title = Text(item, "title");
                if (id <= 0 || string.IsNullOrEmpty(title)) return null;
                var artist = FirstArtist(item);
                return new HomeSectionItem
                {
                    Id = id.ToString(),
                    NumericId = id,
                    Title = title,
                    Subtitle = artist?.Name ?? string.Empty,
                    ArtistId = artist?.Id ?? 0,
                    CoverUrl = JsonImageExtractor.ExtractImageUrl(item) ?? string.Empty,
                    IsExplicit = item.TryGetProperty("explicit", out var e) && e.ValueKind == JsonValueKind.True,
                    ItemType = "album",
                };
            }
            case "playlist":
            {
                var uuid = Text(item, "uuid");
                var title = Text(item, "title");
                if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(title)) return null;
                return new HomeSectionItem
                {
                    Id = uuid,
                    Title = title,
                    Subtitle = PlaylistByline(item),
                    CoverUrl = JsonImageExtractor.ExtractImageUrl(item) ?? string.Empty,
                    ItemType = "playlist",
                };
            }
            case "mix":
            {
                var id = Text(item, "id");
                var title = Text(item, "title");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title)) return null;
                return new HomeSectionItem
                {
                    Id = id,
                    Title = title,
                    Subtitle = Text(item, "subTitle") ?? string.Empty,
                    CoverUrl = JsonImageExtractor.ExtractImageUrl(item) ?? string.Empty,
                    ItemType = "mix",
                };
            }
            case "artist":
            {
                var id = Int(item, "id");
                var name = Text(item, "name");
                if (id <= 0 || string.IsNullOrEmpty(name)) return null;
                return new HomeSectionItem
                {
                    Id = id.ToString(),
                    NumericId = id,
                    Title = name,
                    CoverUrl = TidalApiClient.GetImageUrl(Text(item, "picture"), 320, 320),
                    ItemType = "artist",
                };
            }
            case "link":
            {
                var apiPath = Text(item, "apiPath");
                var title = Text(item, "title");
                if (string.IsNullOrEmpty(apiPath) || string.IsNullOrEmpty(title)) return null;
                return new HomeSectionItem { Id = apiPath, Title = title, ItemType = "link" };
            }
            default:
                return null;
        }
    }

    // Editorial playlists name their creators; user playlists carry a creator object.
    private static string PlaylistByline(JsonElement item)
    {
        if (item.TryGetProperty("creators", out var creators) && creators.ValueKind == JsonValueKind.Array)
        {
            var names = creators.EnumerateArray().Select(c => Text(c, "name")).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (names.Count > 0) return string.Join(", ", names);
        }
        if (item.TryGetProperty("creator", out var creator) && Text(creator, "name") is { Length: > 0 } name)
            return name;
        return string.Empty;
    }

    private static (string Name, int Id)? FirstArtist(JsonElement item)
    {
        if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            && artists.GetArrayLength() > 0)
            return (Text(artists[0], "name") ?? string.Empty, Int(artists[0], "id"));
        if (item.TryGetProperty("artist", out var artist) && artist.ValueKind == JsonValueKind.Object)
            return (Text(artist, "name") ?? string.Empty, Int(artist, "id"));
        return null;
    }

    private static T? Deserialize<T>(JsonElement element) where T : class
    {
        try { return element.Deserialize<T>(TidalApiClient.JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
