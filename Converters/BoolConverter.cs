using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI.Text;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace TidalUi3.Converters;

/// <summary>
/// Universal bool converter that supports multiple conversion types based on the ConverterParameter.
/// Supported parameters: "Visibility", "Visibility,Invert", "Foreground", "Glyph", "Opacity", "FontWeight"
/// </summary>
public class BoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var param = parameter?.ToString()?.ToLowerInvariant() ?? "visibility";
        var isInverted = param.Contains("invert");
        var boolValue = value is true;

        if (isInverted)
            boolValue = !boolValue;

        var mainParam = param.Split(',')[0];

        return mainParam switch
        {
            "foreground" => ConvertToForeground(boolValue),
            "glyph" => ConvertToGlyph(boolValue),
            "opacity" => ConvertToOpacity(boolValue),
            "fontweight" => ConvertToFontWeight(boolValue),
            _ => ConvertToVisibility(boolValue)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var param = parameter?.ToString()?.ToLowerInvariant() ?? "visibility";
        var isInverted = param.Contains("invert");
        var mainParam = param.Split(',')[0];

        bool result = mainParam switch
        {
            "visibility" => value is Visibility.Visible,
            _ => throw new NotSupportedException($"ConvertBack not supported for parameter: {mainParam}")
        };

        return isInverted ? !result : result;
    }

    private static object ConvertToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    private static object ConvertToForeground(bool value) =>
        value
            ? Application.Current.Resources["TextFillColorPrimaryBrush"]
            : Application.Current.Resources["TextFillColorSecondaryBrush"];

    private static object ConvertToGlyph(bool value) =>
        value ? "\uE768" : "\uE8D6"; // Playing : Music note

    private static object ConvertToOpacity(bool value) =>
        value ? 1.0 : 0.5;

    private static object ConvertToFontWeight(bool value) =>
        value ? FontWeights.SemiBold : FontWeights.Normal;
}
