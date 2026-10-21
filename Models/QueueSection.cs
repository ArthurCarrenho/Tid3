namespace TidalUi3.Models;

/// <summary>
/// A section title row in the queue panel. The queue is shown as one list so it can
/// virtualize, and these rows stand in for separate History / Now playing / Up next lists.
/// </summary>
public sealed class QueueSection
{
    public static readonly QueueSection History = new();
    public static readonly QueueSection NowPlaying = new();
    public static readonly QueueSection UpNext = new();

    private QueueSection() { }
}
