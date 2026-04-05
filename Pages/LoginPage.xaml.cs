using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources;

namespace TidalUi3.Pages;

public sealed partial class LoginPage : Page
{
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly DispatcherTimer _expiryTimer;
    private readonly DispatcherTimer _pollTimer;
    private int _secondsRemaining;
    private string? _deviceCode;
    private CancellationTokenSource? _pollCts;
    private readonly ResourceLoader _rl = new();

    public event EventHandler? LoginCompleted;

    public LoginPage()
    {
        InitializeComponent();

        _expiryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _expiryTimer.Tick += ExpiryTimer_Tick;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += PollTimer_Tick;

        Loaded += LoginPage_Loaded;
    }

    private async void LoginPage_Loaded(object sender, RoutedEventArgs e)
    {
        await StartDeviceAuthFlowAsync();
    }

    private async Task StartDeviceAuthFlowAsync()
    {
        StopTimers();
        SetLoading(true);

        try
        {
            var auth = await _api.StartDeviceAuthAsync();

            _deviceCode = auth.DeviceCode;
            DeviceCodeText.Text = auth.UserCode;
            LinkButton.Content = auth.VerificationUri;

            _secondsRemaining = auth.ExpiresIn > 0 ? auth.ExpiresIn : 300;
            UpdateExpiryText();

            if (auth.Interval > 0)
                _pollTimer.Interval = TimeSpan.FromSeconds(auth.Interval);

            SetLoading(false);
            StatusText.Text = _rl.GetString("LoginStatus_Waiting");
            WaitingSpinner.IsActive = true;

            _expiryTimer.Start();
            _pollCts = new CancellationTokenSource();
            _pollTimer.Start();
        }
        catch (Exception ex)
        {
            SetLoading(false);
            ShowError($"Failed to start auth: {ex.Message}");
        }
    }

    private async void PollTimer_Tick(object? sender, object e)
    {
        if (_deviceCode is null || _pollCts is null)
            return;

        try
        {
            var token = await _api.PollDeviceTokenAsync(_deviceCode, _pollCts.Token);
            if (token is null)
                return; // still pending

            // Authorized
            StopTimers();
            WaitingSpinner.IsActive = false;
            StatusText.Text = _rl.GetString("LoginStatus_Success");

            await Task.Delay(800);
            LoginCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException)
        {
            // Code expired
            StopTimers();
            ShowError(_rl.GetString("LoginStatus_Expired"));
        }
        catch (UnauthorizedAccessException)
        {
            StopTimers();
            ShowError(_rl.GetString("LoginStatus_Denied"));
        }
        catch (Exception ex)
        {
            StopTimers();
            ShowError($"Error: {ex.Message}");
        }
    }

    private void ExpiryTimer_Tick(object? sender, object e)
    {
        _secondsRemaining--;
        if (_secondsRemaining <= 0)
        {
            StopTimers();
            ShowError(_rl.GetString("LoginStatus_Expired"));
            return;
        }
        UpdateExpiryText();
    }

    private void UpdateExpiryText()
    {
        var minutes = _secondsRemaining / 60;
        var seconds = _secondsRemaining % 60;
        ExpiryText.Text = $"{_rl.GetString("LoginExpiry_Prefix")}{minutes}:{seconds:D2}";
    }

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(DeviceCodeText.Text);
        Clipboard.SetContent(package);
        CopyButtonText.Text = _rl.GetString("LoginCopy_Copied");

        var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        resetTimer.Tick += (_, _) =>
        {
            CopyButtonText.Text = _rl.GetString("LoginCopy_Default");
            resetTimer.Stop();
        };
        resetTimer.Start();
    }

    private async void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        var uri = LinkButton.Content?.ToString();
        if (!string.IsNullOrEmpty(uri))
        {
            if (!uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                uri = "https://" + uri;
            await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
        }
    }

    private async void RefreshCodeButton_Click(object sender, RoutedEventArgs e)
    {
        await StartDeviceAuthFlowAsync();
    }

    private void StopTimers()
    {
        _expiryTimer.Stop();
        _pollTimer.Stop();
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private void SetLoading(bool loading)
    {
        WaitingSpinner.IsActive = loading;
        StatusText.Text = loading ? _rl.GetString("LoginStatus_Connecting") : "";
        DeviceCodeText.Text = loading ? "-" : DeviceCodeText.Text;
        CopyCodeButton.IsEnabled = !loading;
        RefreshCodeButton.IsEnabled = !loading;
        ExpiryText.Text = "";
    }

    private void ShowError(string message)
    {
        WaitingSpinner.IsActive = false;
        StatusText.Text = message;
        ExpiryText.Text = "";
    }
}
