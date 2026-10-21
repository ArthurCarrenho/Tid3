using System;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TidalUi3.Helpers;

namespace TidalUi3.Pages;

public sealed partial class ExplorePage : Page
{
    private const string ExploreApiPath = "pages/explore";

    private readonly CancellationTokenSource _cts = new();

    public ExplorePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Load();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cts.Cancel();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => Load();

    private async void Load()
    {
        if (!App.ApiClient.IsAuthenticated) return;

        LoadErrorPanel.Visibility = Visibility.Collapsed;
        PageLoading.IsLoading = true;
        try
        {
            await PageSectionsBuilder.LoadAsync(ExploreApiPath, SectionsHost, Frame, _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Exception("ExploreLoadError", ex);
        }
        finally
        {
            PageLoading.IsLoading = false;
            if (SectionsHost.Children.Count == 0 && !_cts.IsCancellationRequested)
                LoadErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
