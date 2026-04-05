using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TidalUi3.Models;

namespace TidalUi3.Services;

public enum RepeatMode { Off, All, One }

public sealed class QueueService
{
    private int _currentIndex = -1;
    private readonly Random _random = new();

    public ObservableCollection<Track> Queue { get; } = [];
    public Track? CurrentTrack => _currentIndex >= 0 && _currentIndex < Queue.Count
        ? Queue[_currentIndex] : null;
    public bool HasTrack => CurrentTrack is not null;
    public int CurrentIndex => _currentIndex;

    public bool Shuffle { get; set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    public event Action<Track?>? CurrentTrackChanged;

    public void PlayTrack(Track track)
    {
        Queue.Clear();
        Queue.Add(track.Clone());
        _currentIndex = 0;
        CurrentTrackChanged?.Invoke(CurrentTrack);
    }

    public bool IsAutoPlayEnabled { get; set; } = true;

    public void PlayTracks(IEnumerable<Track> tracks, int startIndex = 0, bool autoPlay = true)
    {
        IsAutoPlayEnabled = autoPlay;
        Queue.Clear();
        foreach (var t in tracks)
            Queue.Add(t.Clone());
        _currentIndex = startIndex >= 0 && startIndex < Queue.Count ? startIndex : 0;
        CurrentTrackChanged?.Invoke(CurrentTrack);
        IsAutoPlayEnabled = true; // reset
    }

    public void AddToQueue(Track track)
    {
        Queue.Add(track.Clone());
        if (_currentIndex < 0)
        {
            _currentIndex = 0;
            CurrentTrackChanged?.Invoke(CurrentTrack);
        }
    }

    public void SetCurrentIndex(int index)
    {
        if (_currentIndex != index)
        {
            _currentIndex = index;
            CurrentTrackChanged?.Invoke(CurrentTrack);
        }
    }

    public void PlayNext(Track track)
    {
        if (_currentIndex < 0)
        {
            AddToQueue(track);
            return;
        }
        Queue.Insert(_currentIndex + 1, track.Clone());
    }

    public void JumpTo(int index)
    {
        if (index >= 0 && index < Queue.Count)
        {
            _currentIndex = index;
            CurrentTrackChanged?.Invoke(CurrentTrack);
        }
    }

    public bool Next()
    {
        if (Queue.Count == 0) return false;

        if (Repeat == RepeatMode.One)
        {
            CurrentTrackChanged?.Invoke(CurrentTrack);
            return true;
        }

        if (Shuffle)
        {
            _currentIndex = _random.Next(Queue.Count);
            CurrentTrackChanged?.Invoke(CurrentTrack);
            return true;
        }

        if (_currentIndex + 1 < Queue.Count)
        {
            _currentIndex++;
            CurrentTrackChanged?.Invoke(CurrentTrack);
            return true;
        }

        if (Repeat == RepeatMode.All)
        {
            _currentIndex = 0;
            CurrentTrackChanged?.Invoke(CurrentTrack);
            return true;
        }

        return false;
    }

    public bool Previous()
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            CurrentTrackChanged?.Invoke(CurrentTrack);
            return true;
        }

        if (Repeat == RepeatMode.All && Queue.Count > 0)
        {
            _currentIndex = Queue.Count - 1;
            CurrentTrackChanged?.Invoke(CurrentTrack);
            return true;
        }

        return false;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Queue.Count) return;
        Queue.RemoveAt(index);
        if (index < _currentIndex)
            _currentIndex--;
        else if (index == _currentIndex)
        {
            if (_currentIndex >= Queue.Count)
                _currentIndex = Queue.Count - 1;
            CurrentTrackChanged?.Invoke(CurrentTrack);
        }
    }

    public void MoveInQueue(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Queue.Count || newIndex < 0 || newIndex >= Queue.Count || oldIndex == newIndex)
            return;

        var item = Queue[oldIndex];
        Queue.RemoveAt(oldIndex);
        Queue.Insert(newIndex, item);

        // Adjust current index
        if (_currentIndex == oldIndex)
            _currentIndex = newIndex;
        else if (oldIndex < _currentIndex && newIndex >= _currentIndex)
            _currentIndex--;
        else if (oldIndex > _currentIndex && newIndex <= _currentIndex)
            _currentIndex++;
    }

    public void Clear()
    {
        Queue.Clear();
        _currentIndex = -1;
        CurrentTrackChanged?.Invoke(null);
    }
}
