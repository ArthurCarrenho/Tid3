using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using TidalUi3.Helpers;
using TidalUi3.Models;
using TidalUi3.Services;
using Xunit;
using static TidalUi3.Tests.TestTracks;

namespace TidalUi3.Tests;

/// <summary>The queue panel's row list: titles in the right places, kept in sync by small edits.</summary>
public class QueueRowsTests
{
    private readonly QueueService _queue = new();
    private readonly BulkObservableCollection<object> _rows = [];

    // Does what QueuePanelControl.Refresh does and reports the collection events it caused.
    private (int Events, bool Reset) Sync()
    {
        for (var i = 0; i < _queue.Queue.Count; i++)
        {
            _queue.Queue[i].IsPlaying = i == _queue.CurrentIndex;
            _queue.Queue[i].IsPlayed = i < _queue.CurrentIndex;
        }

        var events = 0;
        var reset = false;
        NotifyCollectionChangedEventHandler count = (_, e) =>
        {
            events++;
            reset |= e.Action == NotifyCollectionChangedAction.Reset;
        };
        _rows.CollectionChanged += count;
        QueueRows.Sync(_rows, QueueRows.Build(_queue.Queue, _queue.CurrentIndex));
        _rows.CollectionChanged -= count;
        return (events, reset);
    }

    private void AssertRowsMatchQueue()
    {
        var expected = QueueRows.Build(_queue.Queue, _queue.CurrentIndex);
        Assert.Equal(expected.Count, _rows.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.Same(expected[i], _rows[i]);
    }

    [Fact]
    public void FirstFill_IsOneReset_WithNowPlayingAndUpNextTitles()
    {
        _queue.PlayTracks(Make(2000));

        var (_, reset) = Sync();

        AssertRowsMatchQueue();
        Assert.True(reset);
        Assert.Same(QueueSection.NowPlaying, _rows[0]);
        Assert.Same(QueueSection.UpNext, _rows[2]);
    }

    [Fact]
    public void TypicalEdits_OnABigQueue_TouchOnlyAFewRows()
    {
        _queue.PlayTracks(Make(2000));
        Sync();

        _queue.Next();
        var r = Sync();
        AssertRowsMatchQueue();
        Assert.False(r.Reset);
        Assert.InRange(r.Events, 1, 6);
        Assert.Same(QueueSection.History, _rows[0]);

        _queue.JumpTo(1500);
        r = Sync();
        AssertRowsMatchQueue();
        Assert.False(r.Reset);
        Assert.InRange(r.Events, 1, 6);

        _queue.PlayNext(new Track { Id = 99 });
        r = Sync();
        AssertRowsMatchQueue();
        Assert.InRange(r.Events, 1, 4);

        _queue.RemoveAt(1700);
        Assert.Equal(1, Sync().Events);
        AssertRowsMatchQueue();

        _queue.RemoveAt(10);
        Assert.Equal(1, Sync().Events);
        AssertRowsMatchQueue();

        _queue.AddRange(Make(50));
        r = Sync();
        AssertRowsMatchQueue();
        Assert.False(r.Reset);
        Assert.Equal(50, r.Events);
    }

    [Fact]
    public void BigRewrites_StillEndUpMatching()
    {
        _queue.PlayTracks(Make(500), 100);
        Sync();

        _queue.SetShuffle(true);
        Sync();
        AssertRowsMatchQueue();

        _queue.SetShuffle(false);
        Sync();
        AssertRowsMatchQueue();

        _queue.JumpTo(0);
        Sync();
        AssertRowsMatchQueue();
        Assert.DoesNotContain(QueueSection.History, _rows);

        _queue.PlayTracks(Make(30));
        Assert.True(Sync().Reset);
        AssertRowsMatchQueue();

        _queue.Clear();
        Sync();
        Assert.Empty(_rows);
    }

    [Fact]
    public void Drag_TheOrderReadBackFromTheRowsIsApplied()
    {
        _queue.PlayTracks(Make(10), 3);                 // playing T4
        Sync();

        // As the ListView does on a drop: T8 moved to just below "Up next".
        var t8 = _rows.OfType<Track>().First(t => t.Id == 8);
        _rows.Remove(t8);
        _rows.Insert(_rows.IndexOf(QueueSection.UpNext) + 1, t8);
        _queue.ReorderUpcoming(_rows.OfType<Track>().Where(t => t.IsUpcoming).ToList());
        Sync();
        Assert.Equal(new[] { 1, 2, 3, 4, 8, 5, 6, 7, 9, 10 }, _queue.Ids());
        AssertRowsMatchQueue();

        // Dropped up in the history: it becomes the next track.
        var t10 = _rows.OfType<Track>().First(t => t.Id == 10);
        _rows.Remove(t10);
        _rows.Insert(2, t10);
        _queue.ReorderUpcoming(_rows.OfType<Track>().Where(t => t.IsUpcoming).ToList());
        Sync();
        Assert.Equal(new[] { 1, 2, 3, 4, 10, 8, 5, 6, 7, 9 }, _queue.Ids());
        Assert.Equal(4, _queue.CurrentTrack!.Id);
        AssertRowsMatchQueue();
    }

    [Fact]
    public void RandomEdits_AlwaysConverge()
    {
        var rnd = new Random(1234);
        _queue.PlayTracks(Make(40), 5);
        Sync();
        var nextId = 1000;

        for (var step = 0; step < 3000; step++)
        {
            switch (rnd.Next(9))
            {
                case 0: _queue.Next(); break;
                case 1: _queue.Previous(); break;
                case 2: if (_queue.Queue.Count > 0) _queue.JumpTo(rnd.Next(_queue.Queue.Count)); break;
                case 3: _queue.PlayNext(new Track { Id = nextId++ }); break;
                case 4: _queue.AddRange(Enumerable.Range(0, rnd.Next(1, 8)).Select(_ => new Track { Id = nextId++ })); break;
                case 5: if (_queue.Queue.Count > 1) _queue.RemoveAt(rnd.Next(_queue.Queue.Count)); break;
                case 6: _queue.SetShuffle(!_queue.Shuffle); break;
                case 7: _queue.ReorderUpcoming(_queue.Queue.Skip(_queue.CurrentIndex + 1).OrderBy(_ => rnd.Next()).ToList()); break;
                case 8: if (rnd.Next(20) == 0) _queue.PlayTracks(Make(rnd.Next(1, 60))); break;
            }
            Sync();
            AssertRowsMatchQueue();
        }
    }
}
