using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using TidalUi3.Helpers;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Controls
{
    public sealed partial class PlaybackBarControl : UserControl
    {
        private readonly QueueService _queue = App.Queue;
        private readonly PlaybackService _playback = App.Playback;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiQueue;
        private readonly Windows.ApplicationModel.Resources.ResourceLoader _rl = new();

        // Incremented per cover request so stale loads can tell they were superseded.
        private int _coverRequestId;

        // True between pressing and releasing the progress slider, so the position timer
        // doesn't drag the thumb back while the user is moving it.
        private bool _isDraggingProgress;
        private double _volumeBeforeMute = 0.75;

        // Applies PlaybackService.IsBuffering to the loading ring after a delay, see OnBufferingChanged.
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _loadingRingTimer;

        public event EventHandler? QueueButtonClicked;
        public event EventHandler? LyricsButtonClicked;

        public PlaybackBarControl()
        {
            this.InitializeComponent();
            _uiQueue = DispatcherQueue;

            // Slider marks pointer events as handled internally, so handlers attached in XAML
            // never run. handledEventsToo is the only way to see the user's drag.
            ProgressSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(ProgressSlider_PointerPressed), true);
            ProgressSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(ProgressSlider_PointerReleased), true);
            ProgressSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(ProgressSlider_PointerReleased), true);
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _queue.CurrentTrackChanged += OnCurrentTrackChanged;
            _queue.ModesChanged += OnQueueModesChanged;
            FavoritesService.Changed += UpdateLikeVisual;
            _playback.PositionChanged += OnPositionChanged;
            _playback.PlaybackEnded += OnPlaybackEnded;
            _playback.PlaybackStarted += OnPlaybackStarted;
            _playback.PlaybackPaused += OnPlaybackPaused;
            _playback.VolumeChanged += OnVolumeChanged;
            _playback.BufferingChanged += OnBufferingChanged;

            VolumeControl.Volume = _playback.Volume * 100;
            TransportControls.IsLoading = _playback.IsBuffering;
            UpdateNowPlaying(_queue.CurrentTrack);
            UpdateQualityUI(_playback.CurrentQuality);
            UpdateTransportState();

            // Hook Sync Events
            var main = App.MainWindow;
            if (main != null)
            {
                if (main.Sync != null)
                {
                    OnSyncReady(main.Sync);
                }
                else
                {
                    main.SyncReady += OnSyncReady;
                }
            }
        }

        private void OnSyncReady(SyncService sync)
        {
            _uiQueue.TryEnqueue(() =>
            {
                sync.ConnectionStatusChanged += OnSyncConnectionChanged;
                sync.DevicesUpdated += OnSyncDevicesUpdated;
                UpdateSyncStatus(sync.IsConnected, sync);
                // Seed the list with whatever devices are already known (e.g. from INIT received before this control loaded)
                OnSyncDevicesUpdated(new System.Collections.Generic.List<DeviceInfo>(sync.ActiveDevices));
            });
        }

        private void OnSyncConnectionChanged(bool connected)
        {
            _uiQueue.TryEnqueue(() => UpdateSyncStatus(connected, App.MainWindow?.Sync));
        }

        private void UpdateSyncStatus(bool connected, SyncService? sync)
        {
            // The button stays for the audio output; the sync part shows only with sync on.
            var syncVisibility = SettingsService.SyncEnabled ? Visibility.Visible : Visibility.Collapsed;
            SyncDevicesPanel.Visibility = syncVisibility;
            SyncStatusDot.Visibility = syncVisibility;

            SyncStatusDot.Fill = connected
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 200, 83))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 68, 68));

            // Update "This Device" section in the flyout
            if (sync != null)
                ThisDeviceName.Text = sync.DeviceName;

            ThisDeviceStatus.Text = connected
                ? _rl.GetString("PlaybackBar_Devices_ListeningHere")
                : _rl.GetString("PlaybackBar_Devices_SyncDisconnected");

            ThisDeviceStatus.Foreground = connected
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        }

        private void OnSyncDevicesUpdated(System.Collections.Generic.List<DeviceInfo> devices)
        {
            _uiQueue.TryEnqueue(() =>
            {
                DevicesListView.ItemsSource = devices;
                NoDevicesText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private async void DeviceItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DeviceInfo device)
            {
                var main = App.MainWindow;
                if (main?.Sync != null)
                {
                    await main.Sync.SendCommandAsync(new SyncCommand
                    {
                        Type = "TRANSFER",
                        TargetDeviceId = device.DeviceId
                    });

                    _playback.Pause();
                    DevicesFlyout.Hide();
                }
            }
        }

        private bool _fillingOutputs;

        // Devices come and go; list them as they are each time the flyout opens.
        private async void DevicesFlyout_Opening(object sender, object e)
        {
            var (choices, current) = await _playback.GetOutputChoicesAsync();
            _fillingOutputs = true;
            OutputDevicesList.ItemsSource = choices;
            OutputDevicesList.SelectedIndex = current;
            _fillingOutputs = false;
        }

        private async void OutputDevicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_fillingOutputs || OutputDevicesList.SelectedItem is not AudioOutputDevice device) return;
            await _playback.SetOutputDeviceAsync(device.Id);
        }

        private void SyncSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            DevicesFlyout.Hide();
            App.MainWindow?.NavigateTo(typeof(Pages.SettingsPage), null);
        }

        private void UpdateTransportState()
        {
            TransportControls.IsShuffled = _queue.Shuffle;
            TransportControls.RepeatMode = (Controls.RepeatMode)(int)_queue.Repeat;
        }

        // Shuffle/repeat also change from the pages' shuffle buttons, session restore and sync.
        private void OnQueueModesChanged() => _uiQueue.TryEnqueue(UpdateTransportState);

        private void OnVolumeChanged(double volume) => _uiQueue.TryEnqueue(() => VolumeControl.Volume = volume * 100);

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _queue.CurrentTrackChanged -= OnCurrentTrackChanged;
            _queue.ModesChanged -= OnQueueModesChanged;
            FavoritesService.Changed -= UpdateLikeVisual;
            _playback.PositionChanged -= OnPositionChanged;
            _playback.PlaybackEnded -= OnPlaybackEnded;
            _playback.PlaybackStarted -= OnPlaybackStarted;
            _playback.PlaybackPaused -= OnPlaybackPaused;
            _playback.VolumeChanged -= OnVolumeChanged;
            _playback.BufferingChanged -= OnBufferingChanged;
            _loadingRingTimer?.Stop();

            var main = App.MainWindow;
            if (main?.Sync != null)
            {
                main.Sync.ConnectionStatusChanged -= OnSyncConnectionChanged;
                main.Sync.DevicesUpdated -= OnSyncDevicesUpdated;
            }
        }

        public bool IsLyricsActive
        {
            get => LyricsIcon.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush b && b.Color == (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
            set
            {
                LyricsIcon.Foreground = value
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }
        }

        public bool IsQueueActive
        {
            get => QueueIcon.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush b && b.Color == (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
            set
            {
                QueueIcon.Foreground = value
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }
        }

        public void RefreshQuality()
        {
            UpdateQualityUI(_playback.CurrentQuality);
        }

        private void OnPlaybackStarted() => _uiQueue.TryEnqueue(() => TransportControls.IsPlaying = true);
        private void OnPlaybackPaused() => _uiQueue.TryEnqueue(() => TransportControls.IsPlaying = false);

        /// <summary>
        /// Shows the ring only once loading has gone on for a moment, so a fast load or the
        /// brief buffering of a gapless switch doesn't flash it. Hiding waits a little too:
        /// between fetching the stream and the player opening it, buffering drops for an
        /// instant, and the ring would blink off and on.
        /// </summary>
        private void OnBufferingChanged(bool buffering)
        {
            if (_loadingRingTimer is null)
            {
                _loadingRingTimer = _uiQueue.CreateTimer();
                _loadingRingTimer.IsRepeating = false;
                _loadingRingTimer.Tick += (_, _) => TransportControls.IsLoading = _playback.IsBuffering;
            }

            _loadingRingTimer.Stop();
            _loadingRingTimer.Interval = TimeSpan.FromMilliseconds(buffering ? 400 : 150);
            _loadingRingTimer.Start();
        }

        private void UpdateQualityUI(string quality)
        {
            if (string.IsNullOrEmpty(quality))
            {
                QualityButton.Visibility = Visibility.Collapsed;
                return;
            }

            QualityText.Text = quality;
            var colors = QualityHelper.GetQualityColors(quality);

            QualityButton.Background = colors.BackgroundBrush;
            QualityText.Foreground = colors.ForegroundBrush;
            QualityButton.Visibility = Visibility.Visible;
        }

        private void OnCurrentTrackChanged(Track? track)
        {
            _uiQueue.TryEnqueue(() => UpdateNowPlaying(track));
            if (track is not null)
            {
                _uiQueue.TryEnqueue(() => UpdateQualityUI(_playback.CurrentQuality));
            }
        }

        private void OnPositionChanged(TimeSpan position, TimeSpan duration)
        {
            _uiQueue.TryEnqueue(() =>
            {
                if (_isDraggingProgress) return;
                CurrentTimeText.Text = FormatTime(position);
                if (duration.TotalSeconds > 0)
                {
                    TotalTimeText.Text = FormatTime(duration);
                    ProgressSlider.Value = position.TotalSeconds / duration.TotalSeconds * 100;
                }
            });
        }

        private void OnPlaybackEnded()
        {
            _uiQueue.TryEnqueue(() =>
            {
                ProgressSlider.Value = 0;
                CurrentTimeText.Text = "0:00";
            });
        }

        private static string FormatTime(TimeSpan ts) =>
            ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";

        private async void UpdateNowPlaying(Track? track)
        {
            // Covers resolve at very different speeds (cache hit vs fresh download), so a
            // request for a track that was skipped past can complete after a newer one and
            // overwrite it. Stamp each request and only let the newest one paint.
            var requestId = ++_coverRequestId;

            if (track is null)
            {
                NowPlayingPanel.Visibility = Visibility.Collapsed;
                NowPlayingTitle.Text = "";
                NowPlayingArtist.Text = "";
                NowPlayingCover.Source = null;
                CurrentTimeText.Text = "0:00";
                TotalTimeText.Text = "0:00";
                ProgressSlider.Value = 0;
                TransportControls.IsPlaying = false;
                QualityText.Text = "";
                return;
            }

            NowPlayingPanel.Visibility = Visibility.Visible;
            NowPlayingTitle.Text = track.Title;
            NowPlayingArtist.Text = track.Artist;
            TotalTimeText.Text = track.Duration;

            UpdateLikeVisual();

            var cover = !string.IsNullOrEmpty(track.CoverUrl)
                ? await ImageCacheService.GetImageAsync(track.CoverUrl) : null;

            // A newer track took over while this cover was loading; its art owns the bar.
            if (requestId != _coverRequestId) return;

            NowPlayingCover.Source = cover;
        }

        private void TransportControls_PlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (!_queue.HasTrack) return;
            _playback.TogglePlayPause();
        }

        private void TransportControls_PreviousClick(object sender, RoutedEventArgs e) => _playback.SkipPrevious();
        private void TransportControls_NextClick(object sender, RoutedEventArgs e) => _queue.Next();

        // The button icons follow through ModesChanged.
        private void TransportControls_ShuffleClick(object sender, RoutedEventArgs e) => _queue.SetShuffle(!_queue.Shuffle);

        private void TransportControls_RepeatClick(object sender, RoutedEventArgs e) => _queue.CycleRepeat();

        private void ProgressSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingProgress = true;
            _playback.BeginSeek();
        }

        // Wired to both release and capture-lost, which usually both fire for one drag.
        private void ProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingProgress) return;
            _isDraggingProgress = false;
            _playback.EndSeek(ProgressSlider.Value);
        }

        private void VolumeControl_VolumeChanged(object sender, RoutedEventArgs e) => _playback.Volume = VolumeControl.Volume / 100.0;

        private void VolumeControl_MuteClick(object sender, RoutedEventArgs e)
        {
            if (_playback.Volume > 0)
            {
                _volumeBeforeMute = _playback.Volume;
                _playback.Volume = 0;
            }
            else
            {
                _playback.Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 0.75;
            }
        }

        private async void LikeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_queue.CurrentTrack is not { } track) return;
            // FavoritesService flips the state right away and raises Changed, which repaints
            // the heart; a failed request flips it back the same way.
            await FavoritesService.SetTrackLikedAsync(track, !FavoritesService.IsTrackLiked(track.Id));
        }

        private void UpdateLikeVisual()
        {
            var isLiked = _queue.CurrentTrack is { } track && FavoritesService.IsTrackLiked(track.Id);
            Loc.Label(LikeButton, Loc.Get(isLiked ? "Menu_Unlike" : "Menu_Like"));
            LikeIcon.Glyph = isLiked ? Glyphs.LikeFilled : Glyphs.Like;
            LikeIcon.Foreground = isLiked
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 233, 30, 99))
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        private void LyricsButton_Click(object sender, RoutedEventArgs e) => LyricsButtonClicked?.Invoke(this, EventArgs.Empty);
        private void QueueButton_Click(object sender, RoutedEventArgs e) => QueueButtonClicked?.Invoke(this, EventArgs.Empty);

        private void QualityButton_Click(object sender, RoutedEventArgs e)
        {
            if (_queue.CurrentTrack is null) return;

            var flyout = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top };
            var panel = new StackPanel { Spacing = 8, Padding = new Thickness(8), MaxWidth = 300 };

            panel.Children.Add(new TextBlock { Text = Loc.Get("StreamInfo_Title"), Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = Loc.Format("StreamInfo_Quality", _playback.CurrentPlaybackInfo?.AudioQuality ?? _playback.CurrentQuality), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

            var codec = _playback.CurrentManifest?.Codecs ?? _playback.CurrentPlaybackInfo?.AudioMode ?? "";
            if (!string.IsNullOrEmpty(codec))
                panel.Children.Add(new TextBlock { Text = Loc.Format("StreamInfo_Codec", codec), Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

            var mime = _playback.CurrentManifest?.MimeType ?? _playback.CurrentPlaybackInfo?.ManifestMimeType ?? "";
            if (!string.IsNullOrEmpty(mime))
                panel.Children.Add(new TextBlock { Text = Loc.Format("StreamInfo_Type", mime), Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

            if (_playback.CurrentPlaybackInfo?.AssetPresentation is { Length: > 0 } presentation)
                panel.Children.Add(new TextBlock { Text = Loc.Format("StreamInfo_Format", presentation), Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

            flyout.Content = panel;
            flyout.ShowAt((FrameworkElement)sender);
        }

        private void Context_GoToArtist_Click(object sender, RoutedEventArgs e)
        {
            var track = _queue.CurrentTrack;
            if (track != null && track.ArtistId != 0)
                App.MainWindow?.NavigateTo(typeof(Pages.ArtistPage), track.ArtistId);
        }

        private void Context_GoToAlbum_Click(object sender, RoutedEventArgs e)
        {
            var track = _queue.CurrentTrack;
            if (track != null && track.AlbumId != 0)
                App.MainWindow?.NavigateTo(typeof(Pages.PlaylistDetailPage), track.AlbumId);
        }

        // The two "go to" items are fixed; the rest is rebuilt each time, since the
        // playlists offered are the user's as they are now.
        private void NowPlayingMenu_Opening(object sender, object e)
        {
            var menu = (MenuFlyout)sender;
            while (menu.Items.Count > 2)
                menu.Items.RemoveAt(menu.Items.Count - 1);

            if (_queue.CurrentTrack is not { Id: > 0 } track) return;
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(PlaylistActions.CreateAddToPlaylistMenu([track]));
            var credits = new MenuFlyoutItem { Text = Loc.Get("Menu_Credits"), Icon = new FontIcon { Glyph = Glyphs.Credits } };
            credits.Click += (_, _) => _ = CreditsDialog.ShowAsync(track, (type, parameter) => App.MainWindow?.NavigateTo(type, parameter));
            menu.Items.Add(credits);
        }
    }
}
