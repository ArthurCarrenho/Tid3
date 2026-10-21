using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TidalUi3.Helpers;

/// <summary>
/// Localized strings (Strings/*/Resources.resw) for code outside pages, such as the shared
/// context menus and mappers, which would otherwise each create their own loader.
/// </summary>
public static class Loc
{
    private static readonly ResourceLoader _loader = new();

    public static string Get(string key) => _loader.GetString(key);

    public static string Format(string key, object? arg) => string.Format(_loader.GetString(key), arg);

    /// <summary>
    /// Gives an icon-only control both its tooltip and the name screen readers announce,
    /// for labels that change with state (play/pause, like). Fixed labels use x:Uid instead.
    /// </summary>
    public static void Label(DependencyObject element, string text)
    {
        AutomationProperties.SetName(element, text);
        ToolTipService.SetToolTip(element, text);
    }
}
