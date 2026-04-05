using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TidalUi3.Services;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Resources;

namespace TidalUi3.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _isLoading = true; // prevents event handlers firing during init
    private readonly ResourceLoader _rl = new();

    public SettingsPage()
    {
        InitializeComponent();
        var version = Package.Current.Id.Version;
        VersionText.Text = $"{_rl.GetString("SettingsAbout_Version_Prefix")}{version.Major}.{version.Minor}.{version.Build}";
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isLoading = true;

        // Playback
        PlayRadioSwitch.IsOn = SettingsService.PlayRadioAfterQueue;

        // Audio quality
        var savedQuality = SettingsService.AudioQuality;
        for (int i = 0; i < QualityCombo.Items.Count; i++)
        {
            if (QualityCombo.Items[i] is ComboBoxItem item && item.Tag as string == savedQuality)
            {
                QualityCombo.SelectedIndex = i;
                break;
            }
        }
        if (QualityCombo.SelectedIndex < 0)
            QualityCombo.SelectedIndex = QualityCombo.Items.Count - 1; // default to Max

        // Theme
        var savedTheme = SettingsService.Theme;
        for (int i = 0; i < ThemeCombo.Items.Count; i++)
        {
            if (ThemeCombo.Items[i] is ComboBoxItem item && item.Tag as string == savedTheme)
            {
                ThemeCombo.SelectedIndex = i;
                break;
            }
        }
        if (ThemeCombo.SelectedIndex < 0)
            ThemeCombo.SelectedIndex = 0;

        // Language
        var savedLang = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;
        int langIndex = 0;
        for (int i = 0; i < LanguageCombo.Items.Count; i++)
        {
            if (LanguageCombo.Items[i] is ComboBoxItem item && item.Tag as string == savedLang)
            {
                langIndex = i;
                break;
            }
        }
        LanguageCombo.SelectedIndex = langIndex;

        // Cloud sync
        SyncEnabledSwitch.IsOn = SettingsService.SyncEnabled;
        SyncServerBox.Text = SettingsService.SyncServerUrl;

        // Gapless & Discord
        GaplessSwitch.IsOn = SettingsService.GaplessPlayback;
        DiscordRpcSwitch.IsOn = SettingsService.DiscordRichPresence;

        // General toggles
        NotificationsSwitch.IsOn = SettingsService.ShowNotifications;
        TraySwitch.IsOn = SettingsService.MinimizeToTray;

        // Startup task
        await LoadStartupStateAsync();

        // Last.fm credentials
        LastFmApiKeyBox.Text = SettingsService.LastFmApiKey;
        LastFmApiSecretBox.Password = SettingsService.LastFmApiSecret;

        _isLoading = false;

        UpdateLastFmState();
        await LoadUserProfileAsync();
        await UpdateCacheSizeAsync();

        // Subscribe to sync events for live status updates
        var sync = App.MainWindow?.Sync;
        if (sync != null)
        {
            sync.ConnectionStatusChanged += OnSyncConnectionChanged;
            sync.DevicesUpdated += OnSyncDevicesUpdated;
        }
        UpdateSyncStatusCard();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        var sync = App.MainWindow?.Sync;
        if (sync != null)
        {
            sync.ConnectionStatusChanged -= OnSyncConnectionChanged;
            sync.DevicesUpdated -= OnSyncDevicesUpdated;
        }
    }

    private void OnSyncConnectionChanged(bool connected) => DispatcherQueue.TryEnqueue(UpdateSyncStatusCard);
    private void OnSyncDevicesUpdated(System.Collections.Generic.List<DeviceInfo> _) => DispatcherQueue.TryEnqueue(UpdateSyncStatusCard);

    // ── Audio Quality ──────────────────────────────────────────

    private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (QualityCombo.SelectedItem is ComboBoxItem item && item.Tag is string quality)
            SettingsService.AudioQuality = quality;
    }

    // ── Theme ──────────────────────────────────────────────────

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            SettingsService.Theme = theme;
            App.MainWindow?.ApplyTheme();
        }
    }

    // ── Language ───────────────────────────────────────────────

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            if (Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride != lang)
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
                RestartInfoBar.IsOpen = true;
            }
        }
    }

    // ── Playback ───────────────────────────────────────────────

    private void PlayRadioSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SettingsService.PlayRadioAfterQueue = PlayRadioSwitch.IsOn;
    }

    private void GaplessSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SettingsService.GaplessPlayback = GaplessSwitch.IsOn;
    }

    // ── Cloud Sync ─────────────────────────────────────────────

    private async void SyncEnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SettingsService.SyncEnabled = SyncEnabledSwitch.IsOn;

        if (SyncEnabledSwitch.IsOn)
        {
            await UpdateSyncConnectionAsync();
        }
        else
        {
            App.MainWindow?.Sync?.Disconnect();
            SyncErrorInfoBar.IsOpen = false;
            UpdateSyncStatusCard();
        }
    }

    private async void SyncServerBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (SettingsService.SyncServerUrl == SyncServerBox.Text) return;

        SettingsService.SyncServerUrl = SyncServerBox.Text;
        if (SettingsService.SyncEnabled)
        {
            await UpdateSyncConnectionAsync();
        }
    }

    private async void SyncReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await UpdateSyncConnectionAsync();
    }

    private void UpdateSyncStatusCard()
    {
        var sync = App.MainWindow?.Sync;
        bool enabled = SettingsService.SyncEnabled;

        if (!enabled || sync == null)
        {
            SyncCardDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
            SyncCardStatusText.Text = _rl.GetString("SettingsIntegrations_CloudSync_StatusDisabled");
            SyncCardDeviceText.Text = string.Empty;
            SyncCardDeviceCountText.Visibility = Visibility.Collapsed;
            SyncReconnectButton.Visibility = Visibility.Collapsed;
            return;
        }

        SyncCardDeviceText.Text = $"{_rl.GetString("SettingsIntegrations_CloudSync_ThisDevice")}{sync.DeviceName}";

        if (sync.IsConnected)
        {
            SyncCardDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            SyncCardStatusText.Text = _rl.GetString("SettingsIntegrations_CloudSync_Connected");
            SyncReconnectButton.Visibility = Visibility.Collapsed;

            var count = sync.ConnectedDevicesCount;
            if (count > 0)
            {
                SyncCardDeviceCountText.Text = string.Format(_rl.GetString("SettingsIntegrations_CloudSync_DeviceCount"), count);
                SyncCardDeviceCountText.Visibility = Visibility.Visible;
            }
            else
            {
                SyncCardDeviceCountText.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            SyncCardDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
            SyncCardStatusText.Text = _rl.GetString("SettingsIntegrations_CloudSync_Failed");
            SyncCardDeviceCountText.Visibility = Visibility.Collapsed;
            SyncReconnectButton.Visibility = Visibility.Visible;
        }
    }

    private async Task UpdateSyncConnectionAsync()
    {
        var sync = App.MainWindow?.Sync;
        if (sync == null) return;

        SyncProgressRing.IsActive = true;
        SyncCardStatusText.Text = _rl.GetString("SettingsIntegrations_CloudSync_Testing");
        SyncCardDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
        SyncReconnectButton.Visibility = Visibility.Collapsed;
        SyncErrorInfoBar.IsOpen = false;

        try
        {
            await Task.Delay(400);
            var (success, error) = await sync.ConnectAsync();
            if (!success)
            {
                SyncErrorInfoBar.Message = error ?? "Unknown error";
                SyncErrorInfoBar.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            SyncErrorInfoBar.Message = ex.Message;
            SyncErrorInfoBar.IsOpen = true;
        }
        finally
        {
            SyncProgressRing.IsActive = false;
            UpdateSyncStatusCard();
        }
    }

    // ── Discord ────────────────────────────────────────────────

    private void DiscordRpcSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SettingsService.DiscordRichPresence = DiscordRpcSwitch.IsOn;

        if (DiscordRpcSwitch.IsOn)
        {
            App.DiscordRpc.Connect();
            if (App.Playback.IsPlaying && App.Queue.CurrentTrack != null)
            {
                var pos = App.Playback.Position.TotalSeconds;
                App.DiscordRpc.UpdatePresence(App.Queue.CurrentTrack, true, pos);
            }
        }
        else
        {
            App.DiscordRpc.Disconnect();
        }
    }

    // ── Last.fm ────────────────────────────────────────────────

    private void UpdateLastFmState()
    {
        var hasCredentials = !string.IsNullOrEmpty(SettingsService.LastFmApiKey);
        LastFmConnectButton.IsEnabled = hasCredentials || LastFmService.IsConnected;

        if (LastFmService.IsConnected)
        {
            LastFmConnectButtonText.Text = _rl.GetString("SettingsIntegrations_LastFm_Disconnect");
            LastFmStatusText.Text = _rl.GetString("SettingsIntegrations_LastFm_Connected");
        }
        else
        {
            LastFmConnectButtonText.Text = _rl.GetString("SettingsIntegrations_LastFm_Connect/Text");
            LastFmStatusText.Text = hasCredentials
                ? _rl.GetString("SettingsIntegrations_LastFm_Status/Text")
                : "Enter API credentials below";
        }
    }

    private void LastFmApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SettingsService.LastFmApiKey = LastFmApiKeyBox.Text.Trim();
        UpdateLastFmState();
    }

    private void LastFmApiSecretBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SettingsService.LastFmApiSecret = LastFmApiSecretBox.Password.Trim();
    }

    private async void LastFmConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (LastFmService.IsConnected)
        {
            LastFmService.Logout();
            UpdateLastFmState();
            return;
        }

        LastFmAuthProgress.IsActive = true;
        LastFmAuthProgress.Visibility = Visibility.Visible;
        LastFmConnectButton.IsEnabled = false;
        LastFmStatusText.Text = _rl.GetString("SettingsIntegrations_LastFm_GettingToken");

        try
        {
            var token = await LastFmService.GetAuthTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                LastFmStatusText.Text = _rl.GetString("SettingsIntegrations_LastFm_FailedToken");
                return;
            }

            var authUrl = LastFmService.GetAuthUrl(token);
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(authUrl));

            LastFmStatusText.Text = _rl.GetString("SettingsIntegrations_LastFm_AuthorizePrompt");

            bool authorized = false;
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(2000);
                if (await LastFmService.AuthorizeSessionAsync(token))
                {
                    authorized = true;
                    break;
                }
            }

            if (!authorized)
                LastFmStatusText.Text = _rl.GetString("SettingsIntegrations_LastFm_TimedOut");
        }
        catch (Exception ex)
        {
            LastFmStatusText.Text = $"{_rl.GetString("SettingsIntegrations_LastFm_Error")}{ex.Message}";
        }
        finally
        {
            LastFmAuthProgress.IsActive = false;
            LastFmAuthProgress.Visibility = Visibility.Collapsed;
            LastFmConnectButton.IsEnabled = true;
            UpdateLastFmState();
        }
    }

    // ── Start with Windows ─────────────────────────────────────

    private async Task LoadStartupStateAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync("Tid3Startup");
            StartupSwitch.IsOn = task.State == StartupTaskState.Enabled;
        }
        catch
        {
            StartupSwitch.IsEnabled = false;
        }
    }

    private async void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        try
        {
            var task = await StartupTask.GetAsync("Tid3Startup");
            if (StartupSwitch.IsOn)
            {
                var state = await task.RequestEnableAsync();
                StartupSwitch.IsOn = state == StartupTaskState.Enabled;
            }
            else
            {
                task.Disable();
            }
        }
        catch { }
    }

    // ── Notifications ──────────────────────────────────────────

    private void NotificationsSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SettingsService.ShowNotifications = NotificationsSwitch.IsOn;
    }

    // ── Minimize to Tray ───────────────────────────────────────

    private void TraySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SettingsService.MinimizeToTray = TraySwitch.IsOn;
        // Show/remove tray icon immediately
        if (TraySwitch.IsOn)
            App.MainWindow?.ShowTrayIcon();
        else
            App.MainWindow?.HideTrayIcon();
    }

    // ── Account ────────────────────────────────────────────────

    private async Task LoadUserProfileAsync()
    {
        try
        {
            var user = await App.ApiClient.GetUserProfileAsync();
            UserNameText.Text = $"{user.FirstName} {user.LastName} ({user.Username})".Trim();
            if (string.IsNullOrWhiteSpace(UserNameText.Text))
                UserNameText.Text = _rl.GetString("SettingsAccount_UserDefault");

            UserEmailText.Text = user.Email;
            UserIdText.Text = $"ID {user.UserId}";

            if (App.ApiClient.UserId == 0 && user.UserId != 0)
            {
                App.ApiClient.SetToken(App.ApiClient.AccessToken!, null, user.UserId, App.ApiClient.CountryCode);
            }

            UserCountryText.Text = user.CountryCode;

            if (!string.IsNullOrEmpty(user.Picture))
            {
                var pictureUrl = TidalApiClient.GetImageUrl(user.Picture, 320, 320);
                var cachedImage = await ImageCacheService.GetImageAsync(pictureUrl);
                if (cachedImage != null)
                    UserProfilePicture.ProfilePicture = cachedImage;
            }
        }
        catch
        {
            UserNameText.Text = _rl.GetString("SettingsAccount_FailedLoad");
        }
    }

    // ── Storage ────────────────────────────────────────────────

    private async Task UpdateCacheSizeAsync()
    {
        var sizeBytes = await ImageCacheService.GetCacheSizeAsync();
        var sizeMb = sizeBytes / 1024.0 / 1024.0;
        CacheSizeText.Text = $"{sizeMb:F2}{_rl.GetString("SettingsStorage_Cache_SizeUsed")}";
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        ClearCacheButton.IsEnabled = false;
        await ImageCacheService.ClearCacheAsync();
        await UpdateCacheSizeAsync();
        ClearCacheButton.IsEnabled = true;
    }

    // ── Account Actions ────────────────────────────────────────

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        TokenStorageService.Clear();
        App.Queue.Clear();
        Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
    }

}
