using System.Collections.Generic;
using System.Linq;
using TidalUi3.Models;

namespace TidalUi3.Helpers;

/// <summary>
/// Row list behind the queue panel: every queue track in order, with a
/// <see cref="QueueSection"/> row in front of the history, the current track and the
/// upcoming tracks.
/// </summary>
public static class QueueRows
{
    // Beyond this many row edits an update resets the list instead (new queue, shuffle).
    private const int MaxIncrementalEdits = 100;

    public static List<object> Build(IReadOnlyList<Track> queue, int currentIndex)
    {
        var rows = new List<object>(queue.Count + 3);
        for (var i = 0; i < queue.Count; i++)
        {
            if (i == 0 && currentIndex > 0) rows.Add(QueueSection.History);
            if (i == currentIndex) rows.Add(QueueSection.NowPlaying);
            if (i == currentIndex + 1) rows.Add(QueueSection.UpNext);
            rows.Add(queue[i]);
        }
        return rows;
    }

    /// <summary>
    /// Brings <paramref name="rows"/> in line with <paramref name="target"/> through a few
    /// inserts and removals. Rebuilding the list on every track change would recreate the
    /// visible rows and lose the scroll position. Rows are compared by reference, which works
    /// because the queue holds its own copy of every track.
    /// </summary>
    public static void Sync(BulkObservableCollection<object> rows, List<object> target)
    {
        var inTarget = new HashSet<object>(target, ReferenceEqualityComparer.Instance);
        var leaving = rows.Count(row => !inTarget.Contains(row));
        if (rows.Count == 0 || target.Count == 0 || leaving > MaxIncrementalEdits || leaving * 2 > rows.Count)
        {
            rows.ReplaceAll(target);
            return;
        }

        // A section title sits right before the track it introduces. Take out the titles whose
        // track changed: putting them back is two edits, whereas leaving them would make every
        // track between their old and new place look out of order.
        var titleTargets = new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < target.Count; i++)
            if (target[i] is QueueSection)
                titleTargets[target[i]] = i + 1 < target.Count ? target[i + 1] : null;

        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i] is not QueueSection) continue;
            var next = i + 1 < rows.Count ? rows[i + 1] : null;
            if (!titleTargets.TryGetValue(rows[i], out var expected) || !ReferenceEquals(expected, next))
                rows.RemoveAt(i);
        }

        var edits = 0;
        for (var i = 0; i < target.Count; i++)
        {
            while (i < rows.Count && !inTarget.Contains(rows[i]))
                rows.RemoveAt(i);

            if (i < rows.Count && ReferenceEquals(rows[i], target[i]))
                continue;

            if (++edits > MaxIncrementalEdits)
            {
                rows.ReplaceAll(target);
                return;
            }

            var from = IndexOf(rows, target[i], i + 1);
            if (from >= 0)
                rows.RemoveAt(from);
            rows.Insert(i, target[i]);
        }

        // Whatever is left past the end has left the queue.
        while (rows.Count > target.Count)
            rows.RemoveAt(rows.Count - 1);
    }

    private static int IndexOf(IList<object> rows, object row, int start)
    {
        for (var i = start; i < rows.Count; i++)
            if (ReferenceEquals(rows[i], row)) return i;
        return -1;
    }
}
