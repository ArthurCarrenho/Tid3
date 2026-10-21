using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Data;
using TidalUi3.Models;
using Windows.Foundation;

namespace TidalUi3.Helpers;

/// <summary>
/// A list that fetches its next page when its ListView or GridView scrolls near the end, for
/// results too long to load up front (search results, a genre's "show more").
/// </summary>
public sealed partial class IncrementalCollection<T> : ObservableCollection<T>, ISupportIncrementalLoading
{
    private readonly Func<int, CancellationToken, Task<ItemsPage<T>>> _loadPage;
    private readonly CancellationToken _ct;
    private bool _isLoading;

    public IncrementalCollection(Func<int, CancellationToken, Task<ItemsPage<T>>> loadPage, CancellationToken ct)
    {
        _loadPage = loadPage;
        _ct = ct;
    }

    public bool HasMoreItems { get; private set; } = true;

    /// <summary>Raised on the UI thread when a page fails to load; loading stops there.</summary>
    public event Action<Exception>? LoadFailed;

    /// <summary>Raised after each page, so the page can drop its loading indicator or show "nothing here".</summary>
    public event Action? PageLoaded;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count) => LoadNextPageAsync().AsAsyncOperation();

    private async Task<LoadMoreItemsResult> LoadNextPageAsync()
    {
        if (_isLoading || !HasMoreItems) return new LoadMoreItemsResult { Count = 0 };

        _isLoading = true;
        try
        {
            var page = await _loadPage(Count, _ct);
            foreach (var item in page.Items)
                Add(item);
            // An empty page ends it too, whatever HasMore said, or the list would ask forever.
            HasMoreItems = page.HasMore && page.Items.Count > 0;
            return new LoadMoreItemsResult { Count = (uint)page.Items.Count };
        }
        catch (OperationCanceledException)
        {
            HasMoreItems = false;
            return new LoadMoreItemsResult { Count = 0 };
        }
        catch (Exception ex)
        {
            HasMoreItems = false;
            AppLog.Exception("IncrementalLoadError", ex);
            LoadFailed?.Invoke(ex);
            return new LoadMoreItemsResult { Count = 0 };
        }
        finally
        {
            _isLoading = false;
            PageLoaded?.Invoke();
        }
    }
}
