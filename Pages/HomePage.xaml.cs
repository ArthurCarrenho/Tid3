using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using TidalUi3.Helpers;
using TidalUi3.Services;

namespace TidalUi3.Pages;

public sealed partial class HomePage : Page
{
    private const string ForYouApiPath = "pages/for_you";

    private readonly TidalApiClient _api = App.ApiClient;
    private bool _loaded;

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
    }

    private void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        if (_api.IsAuthenticated)
        {
            LoadSections();
            return;
        }

        // On a first sign-in this page loads behind the login screen, before there is a token,
        // and used to stay empty afterwards. Fill it in as soon as the sign-in completes.
        _api.TokenChanged += OnSignedIn;
        Unloaded += (_, _) => _api.TokenChanged -= OnSignedIn;
    }

    private void OnSignedIn(string accessToken, string? refreshToken, int userId, string countryCode)
    {
        _api.TokenChanged -= OnSignedIn;
        DispatcherQueue.TryEnqueue(LoadSections);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => LoadSections();

    private async void LoadSections()
    {
        if (!_api.IsAuthenticated)
            return;

        LoadErrorPanel.Visibility = Visibility.Collapsed;
        HomeLoading.IsLoading = true;

        try
        {
            await PageSectionsBuilder.LoadAsync(ForYouApiPath, SectionsHost, Frame);
        }
        catch (Exception ex)
        {
            AppLog.Exception("HomeLoadError", ex);
        }
        finally
        {
            HomeLoading.IsLoading = false;
            if (SectionsHost.Children.Count == 0)
                LoadErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
