using System;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TidalUi3.Helpers;
using TidalUi3.Models;

namespace TidalUi3.Pages;

public sealed partial class BrowsePage : Page
{
    private readonly CancellationTokenSource _cts = new();
    private BrowseRequest? _request;

    public BrowsePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not BrowseRequest request) return;
        _request = request;
        TitleText.Text = request.Title;
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
        if (_request is null) return;

        LoadErrorPanel.Visibility = Visibility.Collapsed;
        PageLoading.IsLoading = true;
        try
        {
            if (await PageSectionsBuilder.LoadAsync(_request.ApiPath, SectionsHost, Frame, _cts.Token) is { } title)
                TitleText.Text = title;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Exception("BrowseLoadError", ex);
        }
        finally
        {
            PageLoading.IsLoading = false;
            if (SectionsHost.Children.Count == 0 && !_cts.IsCancellationRequested)
                LoadErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
