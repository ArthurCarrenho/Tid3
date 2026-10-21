using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Helpers;
using TidalUi3.Services;
using Xunit;

namespace TidalUi3.Tests;

/// <summary>The loaders behind "see all": paging through favorites and search results.</summary>
public class PagedLoadersTests
{
    private sealed class FakeEndpoint(int total, params int[][] pages)
    {
        public List<int> Offsets { get; } = [];
        private int _call;

        public Task<TidalPagedResult<int>> Fetch(int limit, int offset, CancellationToken ct)
        {
            Offsets.Add(offset);
            var items = _call < pages.Length ? pages[_call++].ToList() : [];
            return Task.FromResult(new TidalPagedResult<int> { Limit = limit, Offset = offset, TotalNumberOfItems = total, Items = items });
        }
    }

    // Odd numbers stand for entries the mapper can't show, like a playlist entry without its playlist.
    private static string? Map(int value) => value % 2 == 0 ? $"item {value}" : null;

    [Fact]
    public async Task DroppedEntries_StillAdvanceTheServerOffset()
    {
        var endpoint = new FakeEndpoint(6, [0, 1, 2], [4, 5, 6]);
        var load = PagedLoaders.Create<int, string>(endpoint.Fetch, Map);

        var first = await load(0, default);
        // The caller counts the two items it got; the server has to be asked from the third entry on.
        var second = await load(first.Items.Count, default);

        Assert.Equal(["item 0", "item 2"], first.Items);
        Assert.True(first.HasMore);
        Assert.Equal([0, 3], endpoint.Offsets);
        Assert.Equal(["item 4", "item 6"], second.Items);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task EmptyPage_EndsTheList()
    {
        var endpoint = new FakeEndpoint(100, [0, 2]);
        var load = PagedLoaders.Create<int, string>(endpoint.Fetch, Map);

        await load(0, default);
        var next = await load(2, default);

        Assert.Empty(next.Items);
        Assert.False(next.HasMore);
    }

    [Fact]
    public async Task UnknownTotal_FullPageMeansMore_ShortPageMeansDone()
    {
        var full = Enumerable.Range(0, 50).Select(i => i * 2).ToArray();
        var endpoint = new FakeEndpoint(0, full, [0, 2]);
        var load = PagedLoaders.Create<int, string>(endpoint.Fetch, Map);

        Assert.True((await load(0, default)).HasMore);
        Assert.False((await load(50, default)).HasMore);
    }
}
