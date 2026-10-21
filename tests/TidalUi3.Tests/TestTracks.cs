using System.Collections.Generic;
using System.Linq;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Tests;

internal static class TestTracks
{
    /// <summary>Tracks with ids 1..count.</summary>
    public static List<Track> Make(int count) =>
        Enumerable.Range(1, count).Select(i => new Track { Id = i, Title = $"T{i}" }).ToList();

    public static int[] Ids(this QueueService queue) => queue.Queue.Select(t => t.Id).ToArray();
}
