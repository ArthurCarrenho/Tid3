using TidalUi3.Services;
using Xunit;

namespace TidalUi3.Tests;

/// <summary>LRC parsing used for LRCLIB and TIDAL lyrics.</summary>
public class LrcParserTests
{
    [Fact]
    public void ParsesTimestampsInTheShapesSeenInTheWild()
    {
        var lines = LyricsService.ParseLrc(
            "[00:06.73] Empty streets\n[0:09.5] Sleeping\r\n[01:02] Plain seconds\n[02:03.456] Millis");

        Assert.Equal(new[] { 6730, 9500, 62000, 123456 }, lines.ConvertAll(l => l.StartTimeMs));
        Assert.Equal("Empty streets", lines[0].Text);
    }

    [Fact]
    public void SkipsMetadataTags_AndSortsChronologically()
    {
        var lines = LyricsService.ParseLrc("[ar:Artist]\n[ti:Title]\n[00:20.00] second\n[00:10.00] first");

        Assert.Equal(2, lines.Count);
        Assert.Equal("first", lines[0].Text);
        Assert.Equal("second", lines[1].Text);
    }

    [Fact]
    public void KeepsEmptyTimedLines_AsInstrumentalGaps()
    {
        var lines = LyricsService.ParseLrc("[00:01.00] sung\n[00:05.00]\n[00:09.00] sung again");

        Assert.Equal(3, lines.Count);
        Assert.Equal("", lines[1].Text);
    }

    [Fact]
    public void PlainLyrics_AreUnsyncedLines()
    {
        var lines = LyricsService.ParsePlainLyrics("one\ntwo\n\nthree");

        Assert.Equal(4, lines.Count);
        Assert.All(lines, l => Assert.Equal(TidalLyricsSubtitle.Unsynced, l.StartTimeMs));
    }

    [Fact]
    public void NothingToParse_GivesNoLines()
    {
        Assert.Empty(LyricsService.ParseLrc(null));
        Assert.Empty(LyricsService.ParseLrc("   "));
        Assert.Empty(LyricsService.ParsePlainLyrics(""));
    }
}
