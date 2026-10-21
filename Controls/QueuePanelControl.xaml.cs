using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TidalUi3.Helpers;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Controls;

public sealed partial class QueuePanelControl : UserControl
{
    private readonly QueueService _queue;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiQueue;

    // Tracks plus section titles, see QueueRows.
    private readonly BulkObservableCollection<object> _rows = [];

    public event EventHandler? CloseButtonClicked;

    public QueuePanelControl()
    {
        this.InitializeComponent();

        _queue = App.Queue;
        _uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        _queue.CurrentTrackChanged += OnCurrentTrackChanged;
        _queue.Queue.CollectionChanged += OnQueueChanged;

        QueueList.ItemsSource = _rows;

        Refresh();
    }

    private void OnCurrentTrackChanged(Track? track) => ScheduleRefresh();

    private void OnQueueChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => ScheduleRefresh();

    // Adding a radio or an album raises one change per track; rebuilding the list for each
    // of them is wasted work, so collapse a burst into a single refresh.
    private bool _refreshPending;

    private void ScheduleRefresh()
    {
        if (_refreshPending) return;
        _refreshPending = true;
        if (!_uiQueue.TryEnqueue(() =>
            {
                _refreshPending = false;
                Refresh();
            }))
            _refreshPending = false;
    }

    public void Refresh()
    {
        UpdateTrackStates();
        QueueRows.Sync(_rows, QueueRows.Build(_queue.Queue, _queue.CurrentIndex));

        var isEmpty = _queue.Queue.Count == 0;
        QueueList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyQueueMessage.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        QueueClearButton.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Scrolls "Now playing" to the top. Meant for when the panel opens: deep into a long
    /// queue the history above it is hundreds of rows. Not done on every refresh, so browsing
    /// the queue isn't yanked back each time the track changes.
    /// </summary>
    public void ScrollToCurrent()
    {
        if (!_rows.Contains(QueueSection.NowPlaying)) return;

        // The panel was usually collapsed until a moment ago; without a layout pass the list
        // has no viewport yet and ignores the request.
        QueueList.UpdateLayout();
        QueueList.ScrollIntoView(QueueSection.NowPlaying, ScrollIntoViewAlignment.Leading);
    }

    private void UpdateTrackStates()
    {
        var current = _queue.CurrentIndex;
        for (var i = 0; i < _queue.Queue.Count; i++)
        {
            var track = _queue.Queue[i];
            track.IsPlaying = i == current;
            track.IsPlayed = i < current;
        }
    }

    // Containers are recycled between tracks and section titles, so set both ways each time.
    private void QueueList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;

        var isTitle = args.Item is QueueSection;
        var container = args.ItemContainer;
        container.IsHitTestVisible = !isTitle;
        container.IsTabStop = !isTitle;
        if (isTitle)
            container.Padding = new Thickness(0);
        else
            container.ClearValue(Control.PaddingProperty);
    }

    private void QueueClearButton_Click(object sender, RoutedEventArgs e) => _queue.Clear();

    private void QueueCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseButtonClicked?.Invoke(this, EventArgs.Empty);
    }

    private void QueueListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track)
        {
            var idx = _queue.Queue.IndexOf(track);
            if (idx >= 0) _queue.JumpTo(idx);
        }
    }

    // Only upcoming tracks can be reordered; history and the current track stay put.
    private void QueueList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Any(item => item is not Track { IsUpcoming: true }))
            e.Cancel = true;
    }

    private void QueueListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // The ListView already moved the row in _rows. Read the upcoming tracks back in their
        // new order (one dropped above "Now playing" just becomes the next track) and apply
        // that through QueueService, which keeps the current index and shuffle bookkeeping
        // intact. Refresh puts the rows back in line if the queue ignored it.
        _queue.ReorderUpcoming(_rows.OfType<Track>().Where(t => t.IsUpcoming).ToList());
        Refresh();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Track track)
        {
            var idx = _queue.Queue.IndexOf(track);
            if (idx >= 0)
            {
                _queue.RemoveAt(idx);
                Refresh();
            }
        }
    }

    private void Context_GoToArtist_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Track track && track.ArtistId != 0)
        {
            App.MainWindow?.NavigateTo(typeof(Pages.ArtistPage), track.ArtistId);
            CloseButtonClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Context_GoToAlbum_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Track track && track.AlbumId != 0)
        {
            App.MainWindow?.NavigateTo(typeof(Pages.PlaylistDetailPage), track.AlbumId);
            CloseButtonClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Context_Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Track track)
        {
            var idx = _queue.Queue.IndexOf(track);
            if (idx >= 0)
            {
                _queue.RemoveAt(idx);
                Refresh();
            }
        }
    }
}
