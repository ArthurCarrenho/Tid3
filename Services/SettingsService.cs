using Windows.Storage;

namespace TidalUi3.Services;

public static class SettingsService
{
    private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;

    public static bool PlayRadioAfterQueue
    {
        get => Settings.Values["PlayRadioAfterQueue"] is bool b ? b : false;
        set => Settings.Values["PlayRadioAfterQueue"] = value;
    }

    public static bool SyncEnabled
    {
        get => Settings.Values["SyncEnabled"] is bool b ? b : false;
        set => Settings.Values["SyncEnabled"] = value;
    }

    public static string SyncServerUrl
    {
        get => Settings.Values["SyncServerUrl"] as string ?? "wss://your-worker.workers.dev/sync";
        set => Settings.Values["SyncServerUrl"] = value;
    }

    public static string AudioQuality
    {
        get => Settings.Values["AudioQuality"] as string ?? "HI_RES_LOSSLESS";
        set => Settings.Values["AudioQuality"] = value;
    }

    public static string Theme
    {
        get => Settings.Values["Theme"] as string ?? "Default";
        set => Settings.Values["Theme"] = value;
    }

    public static bool ShowNotifications
    {
        get => Settings.Values["ShowNotifications"] is bool b ? b : false;
        set => Settings.Values["ShowNotifications"] = value;
    }

    public static bool MinimizeToTray
    {
        get => Settings.Values["MinimizeToTray"] is bool b ? b : false;
        set => Settings.Values["MinimizeToTray"] = value;
    }

    public static string? LastFmSessionKey
    {
        get => Settings.Values["LastFmSessionKey"] as string;
        set => Settings.Values["LastFmSessionKey"] = value;
    }

    public static string LastFmApiKey
    {
        get => Settings.Values["LastFmApiKey"] as string ?? "";
        set => Settings.Values["LastFmApiKey"] = value;
    }

    public static string LastFmApiSecret
    {
        get => Settings.Values["LastFmApiSecret"] as string ?? "";
        set => Settings.Values["LastFmApiSecret"] = value;
    }

    public static bool GaplessPlayback
    {
        get => Settings.Values["GaplessPlayback"] is bool b ? b : true;
        set => Settings.Values["GaplessPlayback"] = value;
    }

    public static bool DiscordRichPresence
    {
        get => Settings.Values["DiscordRichPresence"] is bool b ? b : false;
        set => Settings.Values["DiscordRichPresence"] = value;
    }
}
