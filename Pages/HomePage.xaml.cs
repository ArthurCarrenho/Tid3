using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using TidalUi3.Helpers;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Pages;

public sealed partial class HomePage : Page
{
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private bool _loaded;
    private readonly ObservableCollection<HomeSection> _sections = new();

    public HomePage()
    {
        InitializeComponent();
        SectionsHost.ItemsSource = _sections;
        Loaded += HomePage_Loaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        if (!_api.IsAuthenticated)
            return;

        HomeLoading.IsLoading = true;

        try
        {
            var forYou = await _api.GetForYouPageAsync();

            if (forYou.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (!row.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var module in modules.EnumerateArray())
                    {
                        if (!module.TryGetProperty("type", out var typeEl))
                            continue;
                        if (!module.TryGetProperty("pagedList", out var pagedList) || !pagedList.TryGetProperty("items", out var items))
                            continue;

                        var type = typeEl.GetString() ?? "";
                        var title = module.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";

                        // If there's a preTitle (e.g. "Because you listened to ..."), use it as a subtitle prefix
                        var preTitle = module.TryGetProperty("preTitle", out var preTitleEl) && preTitleEl.ValueKind == JsonValueKind.String
                            ? preTitleEl.GetString() ?? "" : "";

                        var sectionTitle = !string.IsNullOrEmpty(preTitle) ? preTitle : title;
                        if (string.IsNullOrEmpty(sectionTitle))
                            continue;

                        var section = new HomeSection
                        {
                            Title = sectionTitle,
                            Type = type
                        };

                        if (type == "MIX_LIST" || type == "PLAYLIST_LIST" || type == "USER_MIX_LIST" || type == "USER_PLAYLIST_LIST")
                        {
                            ParseMixItems(items, section);
                        }
                        else if (type == "ALBUM_LIST" || type == "NEW_RELEASE_ALBUM_LIST")
                        {
                            ParseAlbumItems(items, section);
                        }
                        else
                        {
                            // Try to parse as mixes first, fallback skip
                            ParseMixItems(items, section);
                        }

                        if (section.Items.Count > 0)
                            _sections.Add(section);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HomePage error: {ex.Message}");
        }
        finally
        {
            HomeLoading.IsLoading = false;
        }
    }

    private static void ParseMixItems(JsonElement items, HomeSection section)
    {
        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var targetItem = item.TryGetProperty("item", out var nestedItem) ? nestedItem : item;
                var id = targetItem.TryGetProperty("id", out var idEl) ? idEl.ToString() : "";
                var title = targetItem.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
                var subTitle = targetItem.TryGetProperty("subTitle", out var subEl) ? subEl.GetString() ?? "" : "";

                var imageUrl = JsonImageExtractor.ExtractImageUrl(targetItem, 320, 320, "MEDIUM") ?? "";

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(title))
                {
                    section.Items.Add(new HomeSectionItem
                    {
                        Id = id,
                        Title = title,
                        Subtitle = subTitle,
                        CoverUrl = imageUrl,
                        ItemType = "mix"
                    });
                }
            }
            catch { }
        }
    }

    private static void ParseAlbumItems(JsonElement items, HomeSection section)
    {
        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var targetItem = item.TryGetProperty("item", out var nestedItem) ? nestedItem : item;
                var id = targetItem.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                var title = targetItem.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";

                string artist = "";
                int artistId = 0;
                if (targetItem.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                {
                    var first = artists.EnumerateArray().FirstOrDefault();
                    if (first.TryGetProperty("name", out var nameEl))
                        artist = nameEl.GetString() ?? "";
                    if (first.TryGetProperty("id", out var artistIdEl))
                        artistId = artistIdEl.GetInt32();
                }

                var imageUrl = JsonImageExtractor.ExtractImageUrl(targetItem, 320, 320, "MEDIUM") ?? "";

                if (id > 0 && !string.IsNullOrEmpty(title))
                {
                    section.Items.Add(new HomeSectionItem
                    {
                        Id = id.ToString(),
                        NumericId = id,
                        Title = title,
                        Subtitle = artist,
                        CoverUrl = imageUrl,
                        ItemType = "album",
                        ArtistId = artistId
                    });
                }
            }
            catch { }
        }
    }

    private void SectionItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeSectionItem item)
        {
            if (item.ItemType == "mix")
            {
                Frame.Navigate(typeof(PlaylistDetailPage), item.Id);
            }
            else if (item.ItemType == "album" && item.NumericId > 0)
            {
                Frame.Navigate(typeof(PlaylistDetailPage), item.NumericId);
            }
        }
    }

}
