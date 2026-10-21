using System;
using System.Collections.Generic;
using System.Linq;
using TidalUi3.Models;
using TidalUi3.Services;
using Xunit;
using static TidalUi3.Tests.TestTracks;

namespace TidalUi3.Tests;

public class QueueServiceTests
{
    // ── Play next ──

    [Fact]
    public void PlayNext_IsWhatPlaysWhenTheCurrentTrackEnds()
    {
        var q = new QueueService();
        q.PlayTracks(Make(5), 1);                       // playing T2
        q.PlayNext(new Track { Id = 99 });

        Assert.Equal(99, q.Queue[q.PeekAutoNextIndex()].Id);
        q.AdvanceAfterTrackEnded();
        Assert.Equal(99, q.CurrentTrack!.Id);
        q.AdvanceAfterTrackEnded();
        Assert.Equal(3, q.CurrentTrack!.Id);            // then the queue carries on
    }

    [Fact]
    public void PlayNext_UnderShuffle_PlaysNextAndKeepsEveryTrackOnce()
    {
        var q = new QueueService();
        q.PlayShuffled(Make(10));
        q.PlayNext(new Track { Id = 99 });
        q.AdvanceAfterTrackEnded();

        Assert.Equal(99, q.CurrentTrack!.Id);
        Assert.Equal(11, q.Queue.Select(t => t.Id).Distinct().Count());
    }

    // ── Shuffle ──

    [Fact]
    public void Shuffle_KeepsHistoryAndCurrent_AndTurningItOffRestoresTheOrder()
    {
        var q = new QueueService();
        q.PlayTracks(Make(10), 3);                      // playing T4

        q.SetShuffle(true);
        Assert.Equal(4, q.CurrentTrack!.Id);
        Assert.Equal(3, q.CurrentIndex);
        Assert.Equal(new[] { 1, 2, 3, 4 }, q.Queue.Take(4).Select(t => t.Id));

        q.SetShuffle(false);
        Assert.Equal(Enumerable.Range(1, 10), q.Ids());
    }

    [Fact]
    public void ShuffleOff_ContinuesFromTheCurrentTracksOriginalPosition()
    {
        var q = new QueueService();
        q.PlayShuffled(Make(8));
        q.AdvanceAfterTrackEnded();
        var current = q.CurrentTrack!.Id;
        var played = q.Queue.Take(q.CurrentIndex + 1).Select(t => t.Id).ToHashSet();

        q.SetShuffle(false);

        var expected = Enumerable.Range(current + 1, 8 - current)
            .Concat(Enumerable.Range(1, current - 1))
            .Where(i => !played.Contains(i));
        Assert.Equal(expected, q.Queue.Skip(q.CurrentIndex + 1).Select(t => t.Id));
        Assert.Equal(current, q.CurrentTrack!.Id);
    }

    // ── Repeat ──

    [Fact]
    public void Repeat_DecidesWhatComesAfterTheLastTrack()
    {
        var q = new QueueService();
        q.PlayTracks(Make(3), 2);

        Assert.Equal(-1, q.PeekAutoNextIndex());
        q.Repeat = RepeatMode.All;
        Assert.Equal(0, q.PeekAutoNextIndex());
        q.Repeat = RepeatMode.One;
        Assert.Equal(2, q.PeekAutoNextIndex());
    }

    [Fact]
    public void CycleRepeat_StepsOffAllOneAndBack()
    {
        var q = new QueueService();
        var changes = 0;
        q.ModesChanged += () => changes++;

        q.CycleRepeat();
        Assert.Equal(RepeatMode.All, q.Repeat);
        q.CycleRepeat();
        Assert.Equal(RepeatMode.One, q.Repeat);
        q.CycleRepeat();
        Assert.Equal(RepeatMode.Off, q.Repeat);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void RepeatOne_ReplaysOnTrackEnd_ButNextStillLeaves()
    {
        var q = new QueueService();
        q.PlayTracks(Make(3), 2);
        q.Repeat = RepeatMode.One;
        var raised = 0;
        q.CurrentTrackChanged += _ => raised++;

        q.AdvanceAfterTrackEnded();
        Assert.Equal(1, raised);
        Assert.Equal(2, q.CurrentIndex);

        q.JumpTo(0);
        q.Next();
        Assert.Equal(1, q.CurrentIndex);
    }

    // ── Editing ──

    [Fact]
    public void RemoveAt_KeepsOrMovesTheCurrentTrackSensibly()
    {
        var q = new QueueService();
        q.PlayTracks(Make(4), 2);                       // T3

        q.RemoveAt(0);
        Assert.Equal(3, q.CurrentTrack!.Id);
        Assert.Equal(1, q.CurrentIndex);

        q.RemoveAt(1);                                  // the current one: the next takes over
        Assert.Equal(4, q.CurrentTrack!.Id);

        q.RemoveAt(1);                                  // the last one: steps back
        Assert.Equal(2, q.CurrentTrack!.Id);
    }

    [Fact]
    public void ReorderUpcoming_AppliesAValidOrder_AndIgnoresAMismatchedOne()
    {
        var q = new QueueService();
        q.PlayTracks(Make(5), 1);

        q.ReorderUpcoming(q.Queue.Skip(2).Reverse().ToList());
        Assert.Equal(new[] { 1, 2, 5, 4, 3 }, q.Ids());

        q.ReorderUpcoming(new List<Track> { new() { Id = 42 } });
        Assert.Equal(new[] { 1, 2, 5, 4, 3 }, q.Ids());
    }

    [Fact]
    public void PlayTracks_RaisesASingleCollectionEvent()
    {
        var q = new QueueService();
        var events = 0;
        q.Queue.CollectionChanged += (_, _) => events++;

        q.PlayTracks(Make(50));

        Assert.Equal(1, events);
    }

    // ── Restoring ──

    [Fact]
    public void RestoreQueue_KeepsTheOrderAndPassesStartPositionToHandlers()
    {
        var q = new QueueService();
        TimeSpan seen = default;
        var autoPlay = true;
        q.CurrentTrackChanged += _ => { seen = q.StartPosition; autoPlay = q.IsAutoPlayEnabled; };
        q.SetModes(true, RepeatMode.Off);

        q.RestoreQueue(Make(5), 3, autoPlay: false, TimeSpan.FromSeconds(42));

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, q.Ids());
        Assert.Equal(3, q.CurrentIndex);
        Assert.Equal(TimeSpan.FromSeconds(42), seen);
        Assert.False(autoPlay);
        Assert.Equal(TimeSpan.Zero, q.StartPosition);   // only valid during the event
        Assert.True(q.IsAutoPlayEnabled);
    }

    [Fact]
    public void Previous_AtTheStartWithoutRepeat_DoesNothing()
    {
        var q = new QueueService();
        q.PlayTracks(Make(3));

        Assert.False(q.Previous());
        Assert.Equal(0, q.CurrentIndex);
    }

    // ── Lists that arrive in pages ──

    [Fact]
    public void AppendToPlayingList_UnderShuffle_MixesInWithoutMovingTheNextTrack()
    {
        var q = new QueueService();
        var list = Make(200);
        q.PlayShuffled(list.Take(100).ToList());
        var next = q.Queue[q.CurrentIndex + 1];

        q.AppendToPlayingList(list.Skip(100).ToList());

        Assert.Equal(Enumerable.Range(1, 200), q.Queue.Select(t => t.Id).OrderBy(i => i));
        Assert.Same(next, q.Queue[q.CurrentIndex + 1]);
        Assert.Contains(q.Queue.Skip(1).Take(100), t => t.Id > 100);

        // Turning shuffle off goes back to list order, both pages included.
        q.SetShuffle(false);
        var c = q.CurrentTrack!.Id;
        Assert.Equal(Enumerable.Range(c + 1, 200 - c).Concat(Enumerable.Range(1, c - 1)),
            q.Queue.Skip(q.CurrentIndex + 1).Select(t => t.Id));
    }
}
