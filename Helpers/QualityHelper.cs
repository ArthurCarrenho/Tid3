using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Media;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>
/// Quality color definition containing background and foreground brush colors.
/// </summary>
public readonly record struct QualityColors(string BackgroundArgb, string ForegroundArgb)
{
    public SolidColorBrush BackgroundBrush => ColorHelper.GetBrushFromArgb(BackgroundArgb);
    public SolidColorBrush ForegroundBrush => ColorHelper.GetBrushFromArgb(ForegroundArgb);
}

/// <summary>
/// Centralized quality badge formatting and color management based on mediaMetadata.tags.
/// HIRES_LOSSLESS → MAX (Gold), LOSSLESS → HIGH (Cyan), else based on audioQuality fallback.
/// </summary>
public static class QualityHelper
{
    // Color constants for quality badges
    private const string MaxBackground = "#1affd432";
    private const string MaxForeground = "#FFffd432";
    private const string HighBackground = "#1a21feec";
    private const string HighForeground = "#FF33ffee";
    private const string LowBackground = "#1affffff";
    private const string LowForeground = "#FFffffff";

    public static string FormatQuality(TidalMediaMetadata? metadata, string? audioQualityFallback = null)
    {
        if (metadata?.Tags is { Count: > 0 } tags)
        {
            if (tags.Contains("HIRES_LOSSLESS"))
                return "MAX";
            if (tags.Contains("LOSSLESS"))
                return "HIGH";
            if (tags.Contains("MQA"))
                return "MQA";
        }

        // Fallback to audioQuality string
        return audioQualityFallback switch
        {
            "HI_RES_LOSSLESS" => "MAX",
            "HI_RES" => "MAX",
            "LOSSLESS" => "HIGH",
            "HIGH" => "HIGH",
            "LOW" => "LOW",
            _ => audioQualityFallback ?? ""
        };
    }

    public static string FormatQuality(TidalTrack track)
        => FormatQuality(track.MediaMetadata, track.AudioQuality);

    public static string FormatQuality(TidalAlbum album)
        => FormatQuality(album.MediaMetadata, album.AudioQuality);

    /// <summary>
    /// Gets the color scheme for a quality badge.
    /// </summary>
    public static QualityColors GetQualityColors(string quality) => quality switch
    {
        "MAX" => new QualityColors(MaxBackground, MaxForeground),
        "HIGH" => new QualityColors(HighBackground, HighForeground),
        "MQA" => new QualityColors(HighBackground, HighForeground), // MQA uses HIGH colors
        _ => new QualityColors(LowBackground, LowForeground)
    };

    /// <summary>
    /// Gets the background brush for a quality badge.
    /// </summary>
    public static SolidColorBrush GetQualityBackgroundBrush(string quality) =>
        GetQualityColors(quality).BackgroundBrush;

    /// <summary>
    /// Gets the foreground brush for a quality badge.
    /// </summary>
    public static SolidColorBrush GetQualityForegroundBrush(string quality) =>
        GetQualityColors(quality).ForegroundBrush;
}

/// <summary>
/// Helper class for color operations.
/// </summary>
public static class ColorHelper
{
    /// <summary>
    /// Creates a SolidColorBrush from an ARGB hex string (e.g., "#FFffd432" or "#ffd432").
    /// Supports both 8-character (ARGB) and 6-character (RGB) formats.
    /// </summary>
    public static SolidColorBrush GetBrushFromArgb(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        hex = hex.Replace("#", "").Trim();

        // Handle RGB format (6 chars) - assume full opacity
        if (hex.Length == 6)
        {
            byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }

        // Handle ARGB format (8 chars)
        if (hex.Length >= 8)
        {
            byte a = System.Convert.ToByte(hex.Substring(0, 2), 16);
            byte r = System.Convert.ToByte(hex.Substring(2, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(4, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(6, 2), 16);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
        }

        // Fallback for invalid formats
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
