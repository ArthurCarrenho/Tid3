using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TidalUi3.Helpers;
using TidalUi3.Models;

namespace TidalUi3.Pages;

public sealed partial class ItemsGridPage : Page
{
    private readonly CancellationTokenSource _cts = new();

    public ItemsGridPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not ItemsGridRequest request) return;

        TitleText.Text = request.Title;
        var items = new IncrementalCollection<HomeSectionItem>(request.LoadPageAsync, _cts.Token);
        items.PageLoaded += () =>
        {
            LoadingRing.IsActive = items.HasMoreItems;
            LoadingRing.Visibility = items.HasMoreItems ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count == 0 && !items.HasMoreItems && StatusText.Visibility == Visibility.Collapsed)
                ShowStatus(Loc.Get("ItemsGrid_Empty"));
        };
        items.LoadFailed += _ => ShowStatus(Loc.Get("ItemsGrid_LoadFailed"));

        // The GridView asks for the first page itself, and for each next one near the end.
        ItemsGrid.ItemsSource = items;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cts.Cancel();
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    private void ItemsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeSectionItem item)
            ItemNavigator.Open(Frame, item);
    }
}
