using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Controls;

public sealed partial class QueuePanelControl : UserControl
{
    private readonly QueueService _queue;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiQueue;

    // View models for lists
    private readonly ObservableCollection<Track> _historyQueue = new();
    private readonly ObservableCollection<Track> _currentQueue = new();
    private readonly ObservableCollection<Track> _upcomingQueue = new();
    

    public event EventHandler? CloseButtonClicked;

    public QueuePanelControl()
    {
        this.InitializeComponent();
        
        _queue = App.Queue;
        _uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        
        _queue.CurrentTrackChanged += OnCurrentTrackChanged;
        _queue.Queue.CollectionChanged += OnQueueChanged;
        
        HistoryListView.ItemsSource = _historyQueue;
        CurrentListView.ItemsSource = _currentQueue;
        UpcomingListView.ItemsSource = _upcomingQueue;

        Refresh();
    }

    private void OnCurrentTrackChanged(Track? track)
    {
        _uiQueue.TryEnqueue(() => Refresh());
    }

    private void OnQueueChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _uiQueue.TryEnqueue(() => Refresh());
    }

    public void Refresh()
    {
        RefreshQueueTrackStates();
        
        if (_queue.Queue.Count == 0)
        {
            HistorySection.Visibility = Visibility.Collapsed;
            CurrentSection.Visibility = Visibility.Collapsed;
            UpcomingSection.Visibility = Visibility.Collapsed;
            QueueScrollViewer.Visibility = Visibility.Collapsed;
            
            EmptyQueueMessage.Visibility = Visibility.Visible;
            QueueClearButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            QueueScrollViewer.Visibility = Visibility.Visible;
            EmptyQueueMessage.Visibility = Visibility.Collapsed;
            QueueClearButton.Visibility = Visibility.Visible;
        }
    }

    private void RefreshQueueTrackStates()
    {
        _historyQueue.Clear();
        _currentQueue.Clear();
        _upcomingQueue.Clear();

        for (int i = 0; i < _queue.Queue.Count; i++)
        {
            var track = _queue.Queue[i];
            track.IsPlaying = i == _queue.CurrentIndex;
            track.IsPlayed = i < _queue.CurrentIndex;
            track.IsFirstUpcoming = false;

            if (track.IsPlayed)
            {
                _historyQueue.Add(track);
            }
            else if (track.IsPlaying)
            {
                _currentQueue.Add(track);
            }
            else
            {
                _upcomingQueue.Add(track);
            }
        }
        
        HistorySection.Visibility = _historyQueue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CurrentSection.Visibility = _currentQueue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpcomingSection.Visibility = _upcomingQueue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QueueClearButton_Click(object sender, RoutedEventArgs e)
    {
        _queue.Clear();
        Refresh();
    }

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

    private void QueueListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Rebuild the global queue based on the new visual order of _upcomingQueue
        var historyAndCurrent = _queue.Queue.Take(_queue.CurrentIndex + 1).ToList();
        
        _queue.Queue.Clear();
        foreach (var track in historyAndCurrent) _queue.Queue.Add(track);
        foreach (var track in _upcomingQueue) _queue.Queue.Add(track);
        
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
