using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>
/// Page loaders for a page module's "show more": its single-module page first, then the rest of
/// the module through pages/data. Each loader is stateful and meant for one list, asked page
/// after page in order, which is how IncrementalCollection and PlaylistDetailPage use them.
/// </summary>
public static class ShowMoreLoaders
{
    private const int PageSize = 50;

    public static Func<int, CancellationToken, Task<ItemsPage<HomeSectionItem>>> Cards(string showMoreApiPath)
    {
        var source = new ModuleSource(showMoreApiPath);
        return async (offset, ct) =>
        {
            var section = await source.NextAsync(offset, ct);
            return new ItemsPage<HomeSectionItem>(section?.Items ?? [], source.HasMore);
        };
    }

    public static Func<int, CancellationToken, Task<ItemsPage<TidalTrack>>> Tracks(string showMoreApiPath)
    {
        var source = new ModuleSource(showMoreApiPath);
        return async (offset, ct) =>
        {
            var section = await source.NextAsync(offset, ct);
            return new ItemsPage<TidalTrack>(section?.Tracks ?? [], source.HasMore);
        };
    }

    // Offsets count the module's raw entries, not the items kept after parsing: the server pages
    // by its own entries, and the parser drops some (videos).
    private sealed class ModuleSource(string showMoreApiPath)
    {
        private bool _started;
        private string? _dataApiPath;
        private string _moduleType = string.Empty;
        private int _total;
        private int _serverOffset;

        public bool HasMore => !_started || (_dataApiPath is not null && _serverOffset < _total);

        // The caller's offset counts kept items, which isn't what the server pages by; the
        // source keeps its own position instead.
        public async Task<PageSection?> NextAsync(int offset, CancellationToken ct)
        {
            if (!_started)
            {
                _started = true;
                var page = await App.ApiClient.GetPageAsync(showMoreApiPath, ct);
                var data = PageModules.ParseSingleModulePage(page);
                if (data is null)
                {
                    _total = 0;
                    return null;
                }
                (_dataApiPath, _moduleType, _total) = (data.DataApiPath, data.Section.ModuleType, data.TotalItems);
                _serverOffset = data.Section.RawItemCount;
                return data.Section;
            }

            if (!HasMore) return null;
            var json = await App.ApiClient.GetPageDataAsync(_dataApiPath!, _serverOffset, PageSize, ct);
            var section = PageModules.ParseDataPage(json, _moduleType);
            // A page with no entries at all means the total was off; stop rather than ask forever.
            _serverOffset = section.RawItemCount == 0 ? _total : _serverOffset + section.RawItemCount;
            return section;
        }
    }
}
