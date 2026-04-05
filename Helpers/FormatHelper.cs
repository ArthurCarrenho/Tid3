using System;

namespace TidalUi3.Helpers;

/// <summary>
/// Shared formatting helpers used across all pages.
/// </summary>
public static class FormatHelper
{
    public static string FormatDuration(int totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    public static string FormatPlaylistDuration(int totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours} hr {ts.Minutes} min"
            : $"{ts.Minutes} min";
    }
}
