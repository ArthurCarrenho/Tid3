using Microsoft.UI.Xaml.Data;
using System;

namespace TidalUi3.Converters;

public class BoolToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isPlaying && isPlaying)
            return "\uE995"; // Volume icon (playing)
        return "\uE8D6"; // Note icon (default)
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
