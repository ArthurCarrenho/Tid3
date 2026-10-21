using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Controls;

public sealed partial class LyricsPanelControl : UserControl
{
    private const double InactiveFontSize = 18;
    private const double ActiveFontSize = 22;
    private const double InactiveOpacity = 0.5;
    private const double HoverOpacity = 0.8;

    // How long auto-scroll stands down after the user scrolls by hand, so reading ahead
    // isn't yanked back to the current line on the next tick.
    private static readonly TimeSpan AutoScrollSuppression = TimeSpan.FromSeconds(5);

    private readonly LyricsService _lyrics;
    private readonly PlaybackService _playback;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiQueue;

    // The empty state's own title ("No lyrics available"), restored after an instrumental.
    private readonly string _noLyricsTitle;

    // Line TextBlocks in subtitle order, so highlighting never has to walk the visual tree.
    private readonly List<TextBlock> _lineBlocks = [];

    private IReadOnlyList<TidalLyricsSubtitle>? _currentSubtitles;
    private int _lastHighlightedIndex = -1;
    private int _currentLyricsTrackId;
    // True once a request for _currentLyricsTrackId finished, including when it found
    // nothing — otherwise a track without lyrics is re-fetched on every reopen.
    private bool _currentTrackResolved;
    private bool _isSynced;
    private CancellationTokenSource? _loadCts;
    private DateTime _lastUserScrollUtc = DateTime.MinValue;

    public event EventHandler? CloseButtonClicked;

    public LyricsPanelControl()
    {
        this.InitializeComponent();

        _lyrics = App.Lyrics;
        _playback = App.Playback;
        _uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _noLyricsTitle = NoLyricsMessage.Title;

        // The ScrollViewer marks these as handled internally, so handledEventsToo is
        // required to see the user's own scrolling.
        LyricsScrollViewer.AddHandler(PointerWheelChangedEvent,
            new PointerEventHandler(OnUserScrollIntent), true);
        LyricsScrollViewer.AddHandler(PointerPressedEvent,
            new PointerEventHandler(OnUserScrollIntent), true);
    }

    private void OnUserScrollIntent(object sender, PointerRoutedEventArgs e)
        => _lastUserScrollUtc = DateTime.UtcNow;

    private void LyricsCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseButtonClicked?.Invoke(this, EventArgs.Empty);
    }

    public void ClearLyrics()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _currentSubtitles = null;
        _lineBlocks.Clear();
        _lastHighlightedIndex = -1;
        _currentLyricsTrackId = 0;
        _currentTrackResolved = false;
        _isSynced = false;
        _lastUserScrollUtc = DateTime.MinValue;

        if (LyricsContainer != null)
        {
            LyricsContainer.Children.Clear();
            NoLyricsMessage.Visibility = Visibility.Collapsed;
            LyricsLoadingRing.IsActive = false;
            LyricsLoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Loads lyrics for <paramref name="track"/>. LyricsService matches on title, artist,
    /// album and length, so it takes the whole track rather than just its id.
    /// </summary>
    public async Task LoadLyricsAsync(Track track)
    {
        var trackId = track.Id;
        if (_currentLyricsTrackId == trackId && _currentTrackResolved)
            return; // Already resolved for this track, lyrics or not.

        // Skipping tracks quickly leaves earlier requests in flight. Cancel them and, on
        // top of that, check below whether this call is still the current one: without
        // that guard a slow response can render the previous track's lyrics.
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _currentLyricsTrackId = trackId;
        _currentTrackResolved = false;
        _currentSubtitles = null;
        _isSynced = false;
        _lastHighlightedIndex = -1;
        _lastUserScrollUtc = DateTime.MinValue;

        _uiQueue.TryEnqueue(() =>
        {
            LyricsContainer.Children.Clear();
            _lineBlocks.Clear();
            NoLyricsMessage.Visibility = Visibility.Collapsed;
            LyricsLoadingRing.Visibility = Visibility.Visible;
            LyricsLoadingRing.IsActive = true;
        });

        TrackLyrics lyrics;
        try
        {
            lyrics = await _lyrics.GetLyricsAsync(track, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Helpers.AppLog.Exception("LyricsError", ex);
            lyrics = TrackLyrics.None;
        }

        // A newer request took over while this one was in flight; its result owns the panel.
        if (ct.IsCancellationRequested || _currentLyricsTrackId != trackId) return;

        _currentTrackResolved = true;

        if (lyrics.Lines.Count == 0)
        {
            _uiQueue.TryEnqueue(() =>
            {
                if (_currentLyricsTrackId != trackId) return;
                LyricsLoadingRing.IsActive = false;
                LyricsLoadingRing.Visibility = Visibility.Collapsed;
                NoLyricsMessage.Title = lyrics.IsInstrumental
                    ? Helpers.Loc.Get("Lyrics_Instrumental") : _noLyricsTitle;
                NoLyricsMessage.Visibility = Visibility.Visible;
            });
            return;
        }

        // Plain lyrics come back without timings: show the text, but no highlight or seek.
        var subtitles = lyrics.Lines;
        _isSynced = lyrics.IsSynced;
        _currentSubtitles = subtitles;

        _uiQueue.TryEnqueue(() =>
        {
            if (_currentLyricsTrackId != trackId) return;
            RenderLines(subtitles);

            LyricsLoadingRing.IsActive = false;
            LyricsLoadingRing.Visibility = Visibility.Collapsed;

            // Highlight the current position right away, whether or not playback is
            // running — opening the panel while paused should still show where we are.
            if (_isSynced) UpdateLyricsHighlight(_playback.Position);
        });
    }

    private void RenderLines(IReadOnlyList<TidalLyricsSubtitle> subtitles)
    {
        LyricsContainer.Children.Clear();
        _lineBlocks.Clear();

        var inactiveBrush = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];

        for (var i = 0; i < subtitles.Count; i++)
        {
            var line = subtitles[i];
            var textBlock = new TextBlock
            {
                // A blank synced line is an instrumental gap; a blank plain line is just
                // a paragraph break and should stay empty.
                Text = _isSynced && string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text,
                FontSize = InactiveFontSize,
                TextWrapping = TextWrapping.Wrap,
                Foreground = inactiveBrush,
                Padding = new Thickness(4, 6, 4, 6),
                Opacity = _isSynced ? InactiveOpacity : 1.0,
            };

            _lineBlocks.Add(textBlock);

            if (!_isSynced)
            {
                // Nothing to seek to, so no hover affordance and no click target.
                LyricsContainer.Children.Add(textBlock);
                continue;
            }

            var row = new HandCursorGrid
            {
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), // Make hit-testable
            };
            row.Children.Add(textBlock);

            var index = i;
            row.PointerEntered += (_, _) =>
            {
                if (index != _lastHighlightedIndex) textBlock.Opacity = HoverOpacity;
            };
            row.PointerExited += (_, _) =>
            {
                if (index != _lastHighlightedIndex) textBlock.Opacity = InactiveOpacity;
            };
            row.Tapped += (_, _) => _playback.Seek(TimeSpan.FromMilliseconds(line.StartTimeMs));

            LyricsContainer.Children.Add(row);
        }
    }

    public void UpdateLyricsHighlight(TimeSpan position)
    {
        if (!_isSynced || _currentSubtitles is null || _lineBlocks.Count == 0) return;

        var posMs = (int)position.TotalMilliseconds;
        var activeIndex = -1;

        for (var i = _currentSubtitles.Count - 1; i >= 0; i--)
        {
            if (_currentSubtitles[i].StartTimeMs <= posMs)
            {
                activeIndex = i;
                break;
            }
        }

        if (activeIndex == _lastHighlightedIndex) return;

        // Only the line losing the highlight and the one gaining it need restyling.
        ApplyLineStyle(_lastHighlightedIndex, active: false);
        ApplyLineStyle(activeIndex, active: true);
        _lastHighlightedIndex = activeIndex;

        if (activeIndex < 0) return;
        if (DateTime.UtcNow - _lastUserScrollUtc < AutoScrollSuppression) return;

        var activeElement = (FrameworkElement)LyricsContainer.Children[activeIndex];
        var point = activeElement.TransformToVisual(LyricsContainer)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        var targetOffset = point.Y - (LyricsScrollViewer.ViewportHeight / 3);
        LyricsScrollViewer.ChangeView(null, Math.Max(0, targetOffset), null, false);
    }

    private void ApplyLineStyle(int index, bool active)
    {
        if (index < 0 || index >= _lineBlocks.Count) return;

        var textBlock = _lineBlocks[index];
        var resourceKey = active ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush";

        textBlock.FontSize = active ? ActiveFontSize : InactiveFontSize;
        textBlock.FontWeight = active
            ? Microsoft.UI.Text.FontWeights.Bold
            : Microsoft.UI.Text.FontWeights.Normal;
        textBlock.Foreground = (Brush)Application.Current.Resources[resourceKey];
        textBlock.Opacity = active ? 1.0 : InactiveOpacity;
    }
}

public class HandCursorGrid : Microsoft.UI.Xaml.Controls.Grid
{
    public HandCursorGrid()
    {
        this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    }
}
