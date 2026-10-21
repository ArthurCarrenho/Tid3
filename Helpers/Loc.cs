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
}
