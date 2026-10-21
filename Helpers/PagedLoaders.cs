using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>Page loaders over TIDAL's paged endpoints (favorites, search), for ItemsGridPage and track lists.</summary>
public static class PagedLoaders
{
    private const int PageSize = 50;

    /// <summary>
    /// A loader that asks <paramref name="fetch"/> (limit, offset) for one page at a time and
    /// maps each entry, dropping the ones <paramref name="map"/> returns null for. It keeps its
    /// own position on the server: dropped entries would shift the caller's count.
    /// </summary>
    public static Func<int, CancellationToken, Task<ItemsPage<TItem>>> Create<TRaw, TItem>(
        Func<int, int, CancellationToken, Task<TidalPagedResult<TRaw>>> fetch,
        Func<TRaw, TItem?> map)
        where TItem : class
    {
        var serverOffset = 0;
        return async (_, ct) =>
        {
            var page = await fetch(PageSize, serverOffset, ct);
            if (page.Items is not { Count: > 0 })
                return new ItemsPage<TItem>([], false);

            serverOffset += page.Items.Count;
            var items = page.Items.Select(map).Where(i => i is not null).Select(i => i!).ToList();
            var hasMore = page.TotalNumberOfItems > 0
                ? serverOffset < page.TotalNumberOfItems
                : page.Items.Count >= PageSize;
            return new ItemsPage<TItem>(items, hasMore);
        };
    }
}
