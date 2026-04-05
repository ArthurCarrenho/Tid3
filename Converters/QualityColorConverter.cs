using System;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace TidalUi3.Converters;

public class QualityColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string text = value as string;
        string targetProperty = parameter as string; // "Background" or "Foreground"

        string hexColor = "#00000000"; // Default Transparent Fallback

        // 1. Determine the hex color based on the text AND the parameter
        switch (text)
        {
            case "Max":
                hexColor = targetProperty == "Background" ? "#1affd432" : "#ffd432"; // Dark Green BG, White Text
                break;
            case "High":
                hexColor = targetProperty == "Background" ? "#FFF9A825" : "#FF000000"; // Yellow BG, Black Text
                break;
            case "Low":
                hexColor = targetProperty == "Background" ? "#FFC62828" : "#FFFFFFFF"; // Red BG, White Text
                break;
            default:
                hexColor = targetProperty == "Background" ? "#FFCCCCCC" : "#FF333333"; // Gray BG, Dark Gray Text
                break;
        }

        // 2. Convert the #ARGB string to a SolidColorBrush
        return hexColor;
    }

    private SolidColorBrush GetBrushFromARGB(string hex)
    {
        // Remove the # if it exists
        hex = hex.Replace("#", "");

        // Parse the ARGB byte values
        byte a = System.Convert.ToByte(hex.Substring(0, 2), 16);
        byte r = System.Convert.ToByte(hex.Substring(2, 2), 16);
        byte g = System.Convert.ToByte(hex.Substring(4, 2), 16);
        byte b = System.Convert.ToByte(hex.Substring(6, 2), 16);

        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}