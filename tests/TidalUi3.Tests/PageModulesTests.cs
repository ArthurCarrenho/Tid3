using System.Linq;
using System.Text.Json;
using TidalUi3.Helpers;
using Xunit;

namespace TidalUi3.Tests;

/// <summary>Reading TIDAL page JSON, in the shapes the API returns (pages/explore, genre and "for you" pages).</summary>
public class PageModulesTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static string Page(params string[] modules) =>
        "{\"rows\":[" + string.Join(",", modules.Select(m => "{\"modules\":[" + m + "]}")) + "]}";

    [Fact]
    public void ExploreLinks_TitledCloudsAndTheUntitledShortcutRow()
    {
        var page = Json(Page(
            """{"type":"PAGE_LINKS_CLOUD","title":"Gêneros","pagedList":{"items":[{"title":"Hip-hop","icon":"hiphop","apiPath":"pages/genre_hip_hop","imageId":"hiphop"}]}}""",
            """{"type":"PAGE_LINKS","title":"","pagedList":{"items":[{"title":"Novo","icon":"new","apiPath":"pages/explore_new_music","imageId":""}]}}"""));

        var sections = PageModules.Parse(page);

        Assert.Equal(2, sections.Count);
        Assert.All(sections, s => Assert.Equal(PageSectionKind.Links, s.Kind));
        var link = sections[0].Items.Single();
        Assert.Equal("link", link.ItemType);
        Assert.Equal("pages/genre_hip_hop", link.Id);
        Assert.Equal("Hip-hop", link.Title);
    }

    [Fact]
    public void GenrePage_PlaylistsAlbumsArtistsAndTracks_VideosSkipped()
    {
        var page = Json(Page(
            """{"type":"PLAYLIST_LIST","title":"Playlists","showMore":{"title":"Ver tudo","apiPath":"pages/single-module-page/a/1/b/1"},"pagedList":{"items":[{"uuid":"1d3a7aec-f6c4-48af-b10e-55bff96c780a","title":"Rap Hits","squareImage":"aa-bb","creators":[{"name":"TIDAL"}]}]}}""",
            """{"type":"ALBUM_LIST","title":"Novos álbuns","pagedList":{"items":[{"id":42,"title":"Album","cover":"cc-dd","explicit":true,"artists":[{"id":7,"name":"Artist"}]}]}}""",
            """{"type":"ARTIST_LIST","title":"Artistas","pagedList":{"items":[{"id":7,"name":"Artist","picture":"ee-ff"}]}}""",
            """{"type":"TRACK_LIST","title":"Novas músicas","pagedList":{"items":[{"id":5,"title":"Song","duration":180,"artists":[{"id":7,"name":"Artist"}],"album":{"id":42,"title":"Album","cover":"cc-dd"}}]}}""",
            """{"type":"VIDEO_LIST","title":"Vídeos","pagedList":{"items":[{"id":9,"title":"Clip","duration":200}]}}"""));

        var sections = PageModules.Parse(page);

        Assert.Equal(4, sections.Count);

        var playlist = sections[0].Items.Single();
        Assert.Equal("playlist", playlist.ItemType);
        Assert.Equal("1d3a7aec-f6c4-48af-b10e-55bff96c780a", playlist.Id);
        Assert.Equal("TIDAL", playlist.Subtitle);
        Assert.Equal("pages/single-module-page/a/1/b/1", sections[0].ShowMoreApiPath);

        var album = sections[1].Items.Single();
        Assert.Equal(("album", 42, "Artist", 7, true), (album.ItemType, album.NumericId, album.Subtitle, album.ArtistId, album.IsExplicit));
        Assert.Contains("cc/dd", album.CoverUrl);

        var artist = sections[2].Items.Single();
        Assert.Equal(("artist", 7), (artist.ItemType, artist.NumericId));
        Assert.Contains("ee/ff", artist.CoverUrl);

        Assert.Equal(PageSectionKind.Tracks, sections[3].Kind);
        Assert.Equal(5, sections[3].Tracks.Single().Id);
    }

    [Fact]
    public void MixedList_UsesEachEntrysType_AndDropsTracksAmongCards()
    {
        var page = Json(Page(
            """{"type":"MIXED_TYPES_LIST","title":"Para você","pagedList":{"items":[{"type":"MIX","item":{"id":"0123abc","title":"My Mix 1","subTitle":"Artist A","images":{"MEDIUM":{"url":"https://img/m.jpg"}}}},{"type":"ALBUM","item":{"id":3,"title":"Album 3","cover":"x-y"}},{"type":"TRACK","item":{"id":4,"title":"Track"}}]}}"""));

        var items = PageModules.Parse(page).Single().Items;

        Assert.Equal(new[] { "mix", "album" }, items.Select(i => i.ItemType));
        Assert.Equal("0123abc", items[0].Id);
        Assert.Equal("https://img/m.jpg", items[0].CoverUrl);
    }

    [Fact]
    public void UntitledCardRow_AndEmptyModules_AreSkipped()
    {
        var page = Json(Page(
            """{"type":"ALBUM_LIST","title":"","pagedList":{"items":[{"id":1,"title":"A","cover":"c"}]}}""",
            """{"type":"ALBUM_LIST","title":"Vazio","pagedList":{"items":[]}}""",
            """{"type":"FEATURED_PROMOTIONS","title":"Destaques"}"""));

        Assert.Empty(PageModules.Parse(page));
    }

    [Fact]
    public void ShowMorePage_GivesTheModuleAndHowToPageThroughIt()
    {
        var page = Json(Page(
            """{"type":"PLAYLIST_LIST","title":"","pagedList":{"dataApiPath":"pages/data/e57d3ec6","limit":10,"offset":0,"totalNumberOfItems":29,"items":[{"uuid":"u1","title":"P1"}]}}"""));

        var data = PageModules.ParseSingleModulePage(page);

        Assert.NotNull(data);
        Assert.Equal("pages/data/e57d3ec6", data!.DataApiPath);
        Assert.Equal(29, data.TotalItems);
        Assert.Equal("PLAYLIST_LIST", data.Section.ModuleType);
        Assert.Equal("u1", data.Section.Items.Single().Id);

        var next = PageModules.ParseDataPage(Json("""{"items":[{"uuid":"u2","title":"P2"}],"totalNumberOfItems":29}"""), data.Section.ModuleType);
        Assert.Equal("u2", next.Items.Single().Id);
    }
}
