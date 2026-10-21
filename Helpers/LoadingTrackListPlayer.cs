using System.Collections.Generic;
using System.Linq;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>
/// Plays a track list that is still arriving page by page. The queue starts with the tracks
/// loaded so far, and later pages join it as they come in, so playing a long playlist right
/// after opening it doesn't leave the queue cut short.
/// </summary>
public sealed class LoadingTrackListPlayer
{
    private readonly QueueService _queue;
    private bool _isLoading;

    // Last queue entry this list put there. Appending only continues while the queue still
    // ends with it; once something else replaced or extended the queue, it is left alone.
    private Track? _tail;

    public LoadingTrackListPlayer(QueueService queue) => _queue = queue;

    public void Play(IReadOnlyList<Track> loaded, int startIndex)
    {
        _queue.PlayTracks(loaded, startIndex);
        TrackTail();
    }

    public void PlayShuffled(IReadOnlyList<Track> loaded)
    {
        _queue.PlayShuffled(loaded);
        TrackTail();
    }

    /// <summary>Call before the first page is shown when more pages will follow.</summary>
    public void BeginLoading() => _isLoading = true;

    /// <summary>Call with each page added to the list after the first.</summary>
    public void OnPageLoaded(IReadOnlyList<Track> page)
    {
        if (_tail is null || page.Count == 0) return;

        var queue = _queue.Queue;
        if (queue.Count == 0 || !ReferenceEquals(queue[^1], _tail))
        {
            _tail = null;
            return;
        }

        _queue.AppendToPlayingList(page);
        _tail = queue[^1];
    }

    /// <summary>Call once the list is complete, or when loading stopped.</summary>
    public void Complete()
    {
        _isLoading = false;
        _tail = null;
    }

    private void TrackTail() => _tail = _isLoading ? _queue.Queue.LastOrDefault() : null;
}
