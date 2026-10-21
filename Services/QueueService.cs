using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TidalUi3.Helpers;
using TidalUi3.Models;

namespace TidalUi3.Services;

public enum RepeatMode { Off, All, One }

public sealed class QueueService
{
    private readonly BulkObservableCollection<Track> _queue = [];
    private readonly Random _random = new();
    private int _currentIndex = -1;
    private RepeatMode _repeat = RepeatMode.Off;

    // Queue order from before shuffling, so turning shuffle off can put the remaining
    // tracks back the way they were. Empty while shuffle is off.
    private readonly List<Track> _unshuffledOrder = [];

    public ObservableCollection<Track> Queue => _queue;
    public Track? CurrentTrack => _currentIndex >= 0 && _currentIndex < _queue.Count
        ? _queue[_currentIndex] : null;
    public bool HasTrack => CurrentTrack is not null;
    public int CurrentIndex => _currentIndex;

    /// <summary>
    /// Shuffle reorders the upcoming tracks instead of picking a random one on every skip,
    /// so "play next", the queue panel and the gapless preload all agree on what comes next.
    /// Use <see cref="SetShuffle"/> to change it.
    /// </summary>
    public bool Shuffle { get; private set; }

    public RepeatMode Repeat
    {
        get => _repeat;
        set
        {
            if (_repeat == value) return;
            _repeat = value;
            ModesChanged?.Invoke();
        }
    }

    /// <summary>
    /// Whether the track being switched to should start playing right away. Only meaningful
    /// while <see cref="CurrentTrackChanged"/> is being raised.
    /// </summary>
    public bool IsAutoPlayEnabled { get; private set; } = true;

    /// <summary>
    /// Where the track being switched to should start. Same lifetime as <see cref="IsAutoPlayEnabled"/>.
    /// </summary>
    public TimeSpan StartPosition { get; private set; }

    public event Action<Track?>? CurrentTrackChanged;

    /// <summary>Raised when Shuffle or Repeat changes, whoever changed it.</summary>
    public event Action? ModesChanged;

    // ═════════════════════════════════════════════
    //  Starting playback
    // ═════════════════════════════════════════════

    public void PlayTrack(Track track) => PlayTracks([track]);

    public void PlayTracks(IEnumerable<Track> tracks, int startIndex = 0, bool autoPlay = true)
    {
        var list = tracks.Select(t => t.Clone()).ToList();
        var start = startIndex >= 0 && startIndex < list.Count ? startIndex : 0;

        _unshuffledOrder.Clear();
        if (Shuffle && list.Count > 1)
        {
            _unshuffledOrder.AddRange(list);

            // The chosen track plays first, everything else follows in random order.
            var first = list[start];
            list.RemoveAt(start);
            ShuffleInPlace(list);
            list.Insert(0, first);
            start = 0;
        }

        _queue.ReplaceAll(list);
        _currentIndex = list.Count > 0 ? start : -1;
        RaiseCurrentTrackChanged(autoPlay);
    }

    /// <summary>Turns shuffle on and plays the tracks starting from a random one.</summary>
    public void PlayShuffled(IEnumerable<Track> tracks)
    {
        var list = tracks.ToList();
        if (list.Count == 0) return;

        if (!Shuffle)
        {
            Shuffle = true;
            ModesChanged?.Invoke();
        }
        PlayTracks(list, _random.Next(list.Count));
    }

    /// <summary>
    /// Loads a queue exactly as given, without shuffling it again: used to bring back a saved
    /// session or a queue received from another device.
    /// </summary>
    public void RestoreQueue(IEnumerable<Track> tracks, int currentIndex, bool autoPlay, TimeSpan startPosition = default)
    {
        var list = tracks.Select(t => t.Clone()).ToList();
        _unshuffledOrder.Clear();
        _queue.ReplaceAll(list);
        _currentIndex = list.Count == 0 ? -1 : Math.Clamp(currentIndex, 0, list.Count - 1);
        RaiseCurrentTrackChanged(autoPlay, startPosition);
    }

    /// <summary>Sets shuffle and repeat flags without reordering anything.</summary>
    public void SetModes(bool shuffle, RepeatMode repeat)
    {
        if (Shuffle == shuffle && _repeat == repeat) return;
        Shuffle = shuffle;
        _repeat = repeat;
        if (!shuffle) _unshuffledOrder.Clear();
        ModesChanged?.Invoke();
    }

    // ═════════════════════════════════════════════
    //  Editing the queue
    // ═════════════════════════════════════════════

    public void AddToQueue(Track track) => AddRange([track]);

    public void AddRange(IEnumerable<Track> tracks)
    {
        var wasEmpty = _currentIndex < 0;
        foreach (var track in tracks)
        {
            var clone = track.Clone();
            _queue.Add(clone);
            if (Shuffle) _unshuffledOrder.Add(clone);
        }

        if (wasEmpty && _queue.Count > 0)
            MoveTo(0);
    }

    /// <summary>
    /// Adds more tracks of the list that is playing, such as playlist pages that arrived after
    /// playback started. Without shuffle they go at the end. With shuffle they are spread at
    /// random among the upcoming tracks, except the very next one (it may be preloading), and
    /// the pre-shuffle order keeps them in list order.
    /// </summary>
    public void AppendToPlayingList(IEnumerable<Track> tracks)
    {
        if (!Shuffle || _currentIndex < 0)
        {
            AddRange(tracks);
            return;
        }

        var clones = tracks.Select(t => t.Clone()).ToList();
        if (clones.Count == 0) return;
        _unshuffledOrder.AddRange(clones);

        var upcoming = _queue.Skip(_currentIndex + 1).ToList();
        var firstSlot = upcoming.Count > 0 ? 1 : 0;
        foreach (var clone in clones)
            upcoming.Insert(_random.Next(firstSlot, upcoming.Count + 1), clone);
        ReplaceUpcoming(upcoming);
    }

    public void PlayNext(Track track)
    {
        if (_currentIndex < 0)
        {
            AddToQueue(track);
            return;
        }

        var clone = track.Clone();
        if (Shuffle && CurrentTrack is { } current)
            _unshuffledOrder.Insert(_unshuffledOrder.IndexOf(current) + 1, clone);

        _queue.Insert(_currentIndex + 1, clone);
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _queue.Count) return;

        var wasCurrent = index == _currentIndex;
        _unshuffledOrder.Remove(_queue[index]);
        _queue.RemoveAt(index);

        if (index < _currentIndex)
        {
            _currentIndex--;
        }
        else if (wasCurrent)
        {
            // The track that slid into this slot plays instead; past the end, step back.
            if (_currentIndex >= _queue.Count)
                _currentIndex = _queue.Count - 1;
            RaiseCurrentTrackChanged();
        }
    }

    public void MoveInQueue(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _queue.Count || newIndex < 0 || newIndex >= _queue.Count || oldIndex == newIndex)
            return;

        _queue.Move(oldIndex, newIndex);

        if (_currentIndex == oldIndex)
            _currentIndex = newIndex;
        else if (oldIndex < _currentIndex && newIndex >= _currentIndex)
            _currentIndex--;
        else if (oldIndex > _currentIndex && newIndex <= _currentIndex)
            _currentIndex++;
    }

    /// <summary>
    /// Replaces the order of the tracks after the current one, e.g. after a drag in the
    /// queue panel. Ignored if <paramref name="upcoming"/> no longer matches the queue.
    /// </summary>
    public void ReorderUpcoming(IReadOnlyList<Track> upcoming)
    {
        var current = _queue.Skip(_currentIndex + 1).ToHashSet();
        if (upcoming.Count != current.Count || !upcoming.All(current.Contains)) return;
        ReplaceUpcoming(upcoming);
    }

    public void SetShuffle(bool enabled)
    {
        if (Shuffle == enabled) return;
        Shuffle = enabled;

        if (enabled)
        {
            _unshuffledOrder.Clear();
            _unshuffledOrder.AddRange(_queue);
            var upcoming = _queue.Skip(_currentIndex + 1).ToList();
            ShuffleInPlace(upcoming);
            ReplaceUpcoming(upcoming);
        }
        else
        {
            RestoreUnshuffledOrder();
        }

        ModesChanged?.Invoke();
    }

    public void Clear()
    {
        _unshuffledOrder.Clear();
        _queue.Clear();
        _currentIndex = -1;
        RaiseCurrentTrackChanged();
    }

    // ═════════════════════════════════════════════
    //  Moving through the queue
    // ═════════════════════════════════════════════

    /// <summary>
    /// Index the queue moves to when the current track finishes on its own, or -1 when
    /// playback should stop there. Equal to <see cref="CurrentIndex"/> under repeat-one.
    /// </summary>
    public int PeekAutoNextIndex()
    {
        if (_currentIndex < 0 || _queue.Count == 0) return -1;
        if (_repeat == RepeatMode.One) return _currentIndex;
        if (_currentIndex + 1 < _queue.Count) return _currentIndex + 1;
        return _repeat == RepeatMode.All ? 0 : -1;
    }

    /// <summary>Advances after a track played to its end, honouring repeat-one.</summary>
    public bool AdvanceAfterTrackEnded()
    {
        var next = PeekAutoNextIndex();
        if (next < 0) return false;
        MoveTo(next);
        return true;
    }

    /// <summary>
    /// Skips forward on request (next button, media keys). Unlike a natural track end this
    /// leaves the track even under repeat-one.
    /// </summary>
    public bool Next()
    {
        if (_queue.Count == 0) return false;
        if (_currentIndex + 1 < _queue.Count) return MoveTo(_currentIndex + 1);
        if (_repeat == RepeatMode.All) return MoveTo(0);
        return false;
    }

    public bool Previous()
    {
        if (_currentIndex > 0) return MoveTo(_currentIndex - 1);
        if (_repeat == RepeatMode.All && _queue.Count > 0) return MoveTo(_queue.Count - 1);
        return false;
    }

    public void JumpTo(int index)
    {
        if (index >= 0 && index < _queue.Count)
            MoveTo(index);
    }

    /// <summary>Moves to <paramref name="index"/> only if it differs from the current one.</summary>
    public void SetCurrentIndex(int index)
    {
        if (index >= 0 && index < _queue.Count && _currentIndex != index)
            MoveTo(index);
    }

    // ═════════════════════════════════════════════
    //  Internals
    // ═════════════════════════════════════════════

    private bool MoveTo(int index)
    {
        _currentIndex = index;
        RaiseCurrentTrackChanged();
        return true;
    }

    private void RaiseCurrentTrackChanged(bool autoPlay = true, TimeSpan startPosition = default)
    {
        IsAutoPlayEnabled = autoPlay;
        StartPosition = startPosition;
        try
        {
            CurrentTrackChanged?.Invoke(CurrentTrack);
        }
        finally
        {
            IsAutoPlayEnabled = true;
            StartPosition = TimeSpan.Zero;
        }
    }

    private void ReplaceUpcoming(IEnumerable<Track> upcoming)
    {
        var items = _queue.Take(_currentIndex + 1).ToList();
        items.AddRange(upcoming);
        _queue.ReplaceAll(items);
    }

    /// <summary>
    /// Puts the tracks still to play back in their pre-shuffle order, continuing from where
    /// the current track sat in that order. Tracks from before that point go last instead of
    /// being dropped.
    /// </summary>
    private void RestoreUnshuffledOrder()
    {
        var remaining = _queue.Skip(_currentIndex + 1).ToList();
        var remainingSet = remaining.ToHashSet();

        var pivot = CurrentTrack is { } current ? _unshuffledOrder.IndexOf(current) : -1;
        var ordered = _unshuffledOrder.Skip(pivot + 1)
            .Concat(_unshuffledOrder.Take(pivot + 1))
            .Where(remainingSet.Contains)
            .ToList();

        // Anything the saved order doesn't know about keeps its place at the end.
        var known = ordered.ToHashSet();
        ordered.AddRange(remaining.Where(t => !known.Contains(t)));

        _unshuffledOrder.Clear();
        ReplaceUpcoming(ordered);
    }

    private void ShuffleInPlace(List<Track> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
