using System.Linq;
using TidalUi3.Helpers;
using TidalUi3.Models;
using TidalUi3.Services;
using Xunit;
using static TidalUi3.Tests.TestTracks;

namespace TidalUi3.Tests;

/// <summary>Playing a list (playlist, liked songs) that is still arriving page by page.</summary>
public class LoadingTrackListPlayerTests
{
    private readonly QueueService _queue = new();
    private readonly LoadingTrackListPlayer _player;

    public LoadingTrackListPlayerTests() => _player = new LoadingTrackListPlayer(_queue);

    [Fact]
    public void LaterPages_FollowTheTracksAlreadyQueued()
    {
        var list = Make(300);
        _player.BeginLoading();
        _player.Play(list.Take(100).ToList(), 5);
        _player.OnPageLoaded(list.Skip(100).Take(100).ToList());
        _queue.PlayNext(new Track { Id = 99 });         // inserting after current keeps the tail
        _player.OnPageLoaded(list.Skip(200).ToList());
        _player.Complete();

        var expected = new[] { 1, 2, 3, 4, 5, 6, 99 }.Concat(Enumerable.Range(7, 294));
        Assert.Equal(expected, _queue.Ids());
        Assert.Equal(6, _queue.CurrentTrack!.Id);
    }

    [Fact]
    public void Appending_StopsOnceSomethingElseOwnsTheQueue()
    {
        var list = Make(200);
        _player.BeginLoading();
        _player.Play(list.Take(100).ToList(), 0);

        _queue.AddToQueue(new Track { Id = 77 });       // the user added a track at the end
        _player.OnPageLoaded(list.Skip(100).ToList());
        Assert.Equal(101, _queue.Queue.Count);
        Assert.Equal(77, _queue.Queue[^1].Id);

        _queue.PlayTracks(Make(3));                     // the queue was replaced
        _player.OnPageLoaded(list.Skip(100).ToList());
        Assert.Equal(3, _queue.Queue.Count);
    }

    [Fact]
    public void Shuffled_EveryTrackEndsUpQueuedOnce()
    {
        var list = Make(200);
        _player.BeginLoading();
        _player.PlayShuffled(list.Take(100).ToList());
        _player.OnPageLoaded(list.Skip(100).ToList());

        Assert.Equal(Enumerable.Range(1, 200), _queue.Queue.Select(t => t.Id).OrderBy(i => i));
    }

    [Fact]
    public void AListThatWasNeverLoading_IsNotExtended()
    {
        _player.Play(Make(10), 0);                      // an album: complete from the start
        _player.OnPageLoaded(Make(5));

        Assert.Equal(10, _queue.Queue.Count);
    }
}
