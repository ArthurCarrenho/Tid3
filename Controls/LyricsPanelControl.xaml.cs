using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Controls;

public sealed partial class LyricsPanelControl : UserControl
{
    private readonly TidalApiClient _api;
    private readonly PlaybackService _playback;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiQueue;

    private List<TidalLyricsSubtitle>? _currentSubtitles;
    private int _lastHighlightedIndex = -1;
    private int _currentLyricsTrackId;

    public event EventHandler? CloseButtonClicked;

    public LyricsPanelControl()
    {
        this.InitializeComponent();
        
        _api = App.ApiClient;
        _playback = App.Playback;
        _uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    private void LyricsCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseButtonClicked?.Invoke(this, EventArgs.Empty);
    }

    public void ClearLyrics()
    {
        _currentSubtitles = null;
        _lastHighlightedIndex = -1;
        _currentLyricsTrackId = 0;
        if (LyricsContainer != null)
        {
            LyricsContainer.Children.Clear();
            NoLyricsMessage.Visibility = Visibility.Collapsed;
        }
    }

    public async System.Threading.Tasks.Task LoadLyricsAsync(int trackId)
    {
        if (_currentLyricsTrackId == trackId && _currentSubtitles != null)
            return; // Already loaded

        _currentLyricsTrackId = trackId;
        _currentSubtitles = null;
        _lastHighlightedIndex = -1;

        _uiQueue.TryEnqueue(() =>
        {
            LyricsContainer.Children.Clear();
            NoLyricsMessage.Visibility = Visibility.Collapsed;
            LyricsLoadingRing.Visibility = Visibility.Visible;
            LyricsLoadingRing.IsActive = true;
        });

        var subtitles = await _api.GetTrackLyricsAsync(trackId);

        if (subtitles is null || subtitles.Count == 0)
        {
            _uiQueue.TryEnqueue(() => 
            { 
                LyricsLoadingRing.IsActive = false;
                LyricsLoadingRing.Visibility = Visibility.Collapsed;
                NoLyricsMessage.Visibility = Visibility.Visible; 
            });
            return;
        }

        _currentSubtitles = subtitles;

        _uiQueue.TryEnqueue(() =>
        {
            LyricsContainer.Children.Clear();
            for (int i = 0; i < subtitles.Count; i++)
            {
                var line = subtitles[i];
                var tb = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text,
                    FontSize = 18,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    Padding = new Thickness(4, 6, 4, 6),
                    Tag = i,
                    IsHitTestVisible = true,
                    Opacity = 0.5
                };

                var border = new HandCursorGrid
                {
                    Padding = new Thickness(0),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) // Make hit-testable
                };
                border.Children.Add(tb);

                border.PointerEntered += (s, e) => { if (s is Grid b && b.Children.Count > 0 && b.Children[0] is TextBlock t && (int)t.Tag != _lastHighlightedIndex) t.Opacity = 0.8; };
                border.PointerExited += (s, e) => { if (s is Grid b && b.Children.Count > 0 && b.Children[0] is TextBlock t && (int)t.Tag != _lastHighlightedIndex) t.Opacity = 0.5; };
                
                border.Tapped += LyricLine_Tapped;
                LyricsContainer.Children.Add(border);
            }

            LyricsLoadingRing.IsActive = false;
            LyricsLoadingRing.Visibility = Visibility.Collapsed;

            // Immediately highlight current position
            if (_playback.IsPlaying)
                UpdateLyricsHighlight(_playback.Position);
        });
    }

    public void UpdateLyricsHighlight(TimeSpan position)
    {
        if (_currentSubtitles is null || LyricsContainer.Children.Count == 0) return;

        var posMs = (int)position.TotalMilliseconds;
        int activeIndex = -1;

        for (int i = _currentSubtitles.Count - 1; i >= 0; i--)
        {
            if (_currentSubtitles[i].StartTimeMs <= posMs)
            {
                activeIndex = i;
                break;
            }
        }

        if (activeIndex == _lastHighlightedIndex) return;
        _lastHighlightedIndex = activeIndex;

        for (int i = 0; i < LyricsContainer.Children.Count; i++)
        {
            var element = LyricsContainer.Children[i] as FrameworkElement;
            if (element is Grid g && g.Children.Count > 0) element = g.Children[0] as FrameworkElement;

            if (element is TextBlock tb)
            {
                if (i == activeIndex)
                {
                    tb.FontSize = 22;
                    tb.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                    tb.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
                    tb.Opacity = 1.0;
                }
                else
                {
                    tb.FontSize = 18;
                    tb.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
                    tb.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
                    tb.Opacity = 0.5;
                }
            }
        }

        // Auto-scroll to active line
        if (activeIndex >= 0 && activeIndex < LyricsContainer.Children.Count)
        {
            var activeElement = (FrameworkElement)LyricsContainer.Children[activeIndex];
            var transform = activeElement.TransformToVisual(LyricsContainer);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var targetOffset = point.Y - (LyricsScrollViewer.ViewportHeight / 3);
            LyricsScrollViewer.ChangeView(null, Math.Max(0, targetOffset), null, false);
        }
    }

    private void LyricLine_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var element = sender as FrameworkElement;
        if (element is Grid g && g.Children.Count > 0) element = g.Children[0] as FrameworkElement;

        if (element is TextBlock tb && tb.Tag is int index && _currentSubtitles is not null && index < _currentSubtitles.Count)
        {
            var line = _currentSubtitles[index];
            _playback.Seek(TimeSpan.FromMilliseconds(line.StartTimeMs));
        }
    }


}

public class HandCursorGrid : Microsoft.UI.Xaml.Controls.Grid
{
    public HandCursorGrid()
    {
        this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    }
}
