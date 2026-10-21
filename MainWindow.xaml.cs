using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Collections.ObjectModel;
using TidalUi3.Helpers;
using TidalUi3.Models;
using TidalUi3.Pages;
using TidalUi3.Services;
using Microsoft.UI.Input;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace TidalUi3;

public sealed partial class MainWindow : Window
{
    private readonly QueueService _queue = App.Queue;
    private readonly PlaybackService _playback = App.Playback;
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiQueue;
    private readonly Microsoft.Windows.ApplicationModel.Resources.ResourceLoader _resourceLoader = new();

    // A track that fails is skipped, but after this many in a row the problem isn't the
    // track (offline, TIDAL down), so playback stops instead of burning through the queue.
    private const int MaxConsecutivePlaybackFailures = 3;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _playbackMessageTimer;

    // Set by anything the saved session holds, so an idle, paused app stops rewriting it.
    private bool _sessionDirty = true;

    // Pending sync broadcast, see BroadcastState.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _broadcastTimer;

    // Queue panel state
    private bool _queuePanelVisible;

    // Lyrics state
    private bool _lyricsVisible;

    // Taskbar thumbnail toolbar
    private readonly ThumbnailToolbar _thumbToolbar = new();

    // Scrobbling state
    private Track? _scrobbleTrack;
    private DateTimeOffset _scrobbleStartTime;
    private TimeSpan _scrobbleListenedTime;
    private bool _scrobbleIsPlaying;

    // System tray
    private readonly TrayIconHelper _trayIcon = new();
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Title = "Tid3";
        AppWindow.SetIcon("Assets/Tid3.ico");
        _uiQueue = DispatcherQueue;
        ApplyTheme();

        // A click anywhere on the page closes the search suggestions, even where nothing
        // takes focus. handledEventsToo, because list items and buttons handle the press.
        ContentFrame.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => PageHeader.CloseSuggestions()), true);

        _queue.CurrentTrackChanged += OnCurrentTrackChanged;
        _queue.Queue.CollectionChanged += (_, _) => _sessionDirty = true;
        _queue.ModesChanged += () => _sessionDirty = true;
        _playback.PositionChanged += OnPositionChanged;
        _playback.PlaybackEnded += OnPlaybackEnded;
        _playback.PlaybackFailed += OnPlaybackFailed;
        _playback.PlaybackStarted += () => { _sessionDirty = true; BroadcastState(); ScrobbleResume(); UpdateDiscordPresence(true); };
        _playback.PlaybackPaused += () => { _sessionDirty = true; BroadcastState(); ScrobblePause(); UpdateDiscordPresence(false); };
        _playback.VolumeChanged += (_) => { _sessionDirty = true; BroadcastState(); };
        _playback.SmtcNext += () => _uiQueue.TryEnqueue(() => _queue.Next());
        _playback.SmtcPrevious += () => _uiQueue.TryEnqueue(() => _playback.SkipPrevious());
        _playback.PlaybackStarted += () => _uiQueue.TryEnqueue(() => _thumbToolbar.UpdatePlayPauseIcon(true));
        _playback.PlaybackPaused += () => _uiQueue.TryEnqueue(() => _thumbToolbar.UpdatePlayPauseIcon(false));

        // Taskbar thumbnail toolbar (prev/pause/next)
        _thumbToolbar.PreviousClicked += () => _uiQueue.TryEnqueue(() => _playback.SkipPrevious());
        _thumbToolbar.PlayPauseClicked += () => _uiQueue.TryEnqueue(() => _playback.TogglePlayPause());
        _thumbToolbar.NextClicked += () => _uiQueue.TryEnqueue(() => _queue.Next());
        _thumbToolbar.Initialize(this);

        // Minimize to tray
        if (SettingsService.MinimizeToTray)
            _trayIcon.Show(this);

        _trayIcon.ShowRequested += () => _uiQueue.TryEnqueue(BringToFront);
        _trayIcon.QuitRequested += () => _uiQueue.TryEnqueue(() =>
        {
            _forceClose = true;
            _trayIcon.Dispose();
            Close();
        });

        AppWindow.Closing += (_, args) =>
        {
            if (SettingsService.MinimizeToTray && !_forceClose)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
            else
            {
                SaveSessionOnExit();
                _trayIcon.Dispose();
                _thumbToolbar.Dispose();
                _syncService?.Dispose();
                App.DiscordRpc.Dispose();
            }
        };

        TryRestoreOrShowLogin();
    }

    private void UpdateDiscordPresence(bool isPlaying)
    {
        if (SettingsService.DiscordRichPresence)
        {
            var track = _queue.CurrentTrack;
            if (track != null)
            {
                var position = _playback.Position.TotalSeconds;
                App.DiscordRpc.UpdatePresence(track, isPlaying, position);
            }
            else
            {
                App.DiscordRpc.ClearPresence();
            }
        }
        else
        {
            App.DiscordRpc.Disconnect();
        }
    }

    /// <summary>
    /// Publishes the playback state to the other devices. Dragging the volume slider or a
    /// burst of play/pause calls this many times a second, and every update carries the
    /// whole queue, so calls within a short window collapse into one send.
    /// </summary>
    private void BroadcastState()
    {
        if (!_uiQueue.HasThreadAccess)
        {
            _uiQueue.TryEnqueue(BroadcastState);
            return;
        }
        if (_syncService is not { IsConnected: true }) return;

        if (_broadcastTimer is null)
        {
            _broadcastTimer = _uiQueue.CreateTimer();
            _broadcastTimer.Interval = TimeSpan.FromMilliseconds(300);
            _broadcastTimer.IsRepeating = false;
            _broadcastTimer.Tick += (_, _) => SendStateNow();
        }
        if (!_broadcastTimer.IsRunning)
            _broadcastTimer.Start();
    }

    private async void SendStateNow()
    {
        if (_syncService != null && _syncService.IsConnected)
        {
            try
            {
                await _syncService.UpdateStateAsync(new SyncState
                {
                    Queue = new List<Track>(_queue.Queue),
                    CurrentIndex = _queue.CurrentIndex,
                    PositionSeconds = _playback.Position.TotalSeconds,
                    DurationSeconds = _playback.Duration.TotalSeconds,
                    Volume = _playback.Volume,
                    IsPlaying = _playback.IsPlaying,
                    Shuffle = _queue.Shuffle,
                    Repeat = _queue.Repeat,
                    ActiveDeviceId = _syncService.DeviceId,
                    ActiveDeviceName = Environment.MachineName
                });
            }
            catch
            {
                // The socket can drop between the IsConnected check and the send. An
                // exception escaping this async void would take the whole app down.
            }
        }
    }

    // ?? Auth ??

    private async void TryRestoreOrShowLogin()
    {
        var api = App.ApiClient;
        if (!api.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        try
        {
            await api.RefreshAccessTokenAsync();
        }
        catch (System.Net.Http.HttpRequestException ex)
            when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
        {
            // TIDAL rejected the refresh token, so the session really is over.
            api.SignOut();
            TokenStorageService.Clear();
            ShowLogin();
            return;
        }
        catch
        {
            // Offline or TIDAL unreachable. That is no reason to sign out: keep the session,
            // requests refresh the token by themselves once the connection is back.
        }
        ShowMainApp();
    }

    private void ShowLogin()
    {
        var loginPage = new LoginPage();
        loginPage.LoginCompleted += OnLoginCompleted;
        LoginFrame.Content = loginPage;
        LoginFrame.Visibility = Visibility.Visible;
        MainAppPanel.Visibility = Visibility.Collapsed;
    }

    private void OnLoginCompleted(object? sender, EventArgs e)
    {
        if (sender is LoginPage lp) lp.LoginCompleted -= OnLoginCompleted;
        ShowMainApp();
    }

    public event Action<SyncService>? SyncReady;
    public SyncService? Sync => _syncService;
    private SyncService? _syncService;

    private async void ShowMainApp()
    {
        LoginFrame.Content = null;
        LoginFrame.Visibility = Visibility.Collapsed;
        MainAppPanel.Visibility = Visibility.Visible;
        PageHeader.ApiClient = _api;
        PageHeader.NavigationRequested += (pageType, param) => ContentFrame.Navigate(pageType, param);
        LoadSidebarPlaylists();
        _ = FavoritesService.LoadAsync();

        if (!_queue.HasTrack)
        {
            var session = await SessionStorageService.LoadAsync();
            if (session != null && session.Queue.Count > 0)
            {
                _playback.Volume = session.Volume;
                // The saved order already reflects shuffle, so restore it untouched and
                // only then bring the flags back. The track opens at the saved position.
                _queue.RestoreQueue(session.Queue, session.CurrentIndex, autoPlay: false,
                    TimeSpan.FromSeconds(Math.Max(0, session.PositionSeconds)));
                _queue.SetModes(session.Shuffle, session.Repeat);
            }
        }

        // Initialize Sync
        if (_api.UserId != 0)
        {
            _syncService = new SyncService(_api.UserId);
            _syncService.StateReceived += OnSyncStateReceived;
            _syncService.CommandReceived += OnSyncCommandReceived;
            SyncReady?.Invoke(_syncService);
            await _syncService.ConnectAsync();
        }

        StartSessionSaveLoop();
    }

    private bool _isSessionLoopRunning;
    private async void StartSessionSaveLoop()
    {
        if (_isSessionLoopRunning) return;
        _isSessionLoopRunning = true;
        while (true)
        {
            await System.Threading.Tasks.Task.Delay(10000);
            if (!App.ApiClient.IsAuthenticated) continue;

            // While playing the position moves, so there is always something to save. Paused
            // with nothing changed, the file already holds this exact state.
            if (!_playback.IsPlaying && !_sessionDirty) continue;
            _sessionDirty = false;

            try
            {
                var state = CaptureSessionState();

                // Serializing a queue of thousands of tracks takes a while; keep it off the UI
                // thread. The state holds its own copy of the queue list.
                await System.Threading.Tasks.Task.Run(() => SessionStorageService.SaveAsync(state));

                // Only the device that is actually playing publishes its state. An idle device
                // pushing its stale queue every 10 s would overwrite edits made on the active one.
                if (_syncService != null && _syncService.IsConnected && state.IsPlaying)
                {
                    await _syncService.UpdateStateAsync(new SyncState
                    {
                        Queue = state.Queue,
                        CurrentIndex = state.CurrentIndex,
                        PositionSeconds = state.PositionSeconds,
                        DurationSeconds = state.DurationSeconds,
                        Volume = state.Volume,
                        IsPlaying = state.IsPlaying,
                        Shuffle = state.Shuffle,
                        Repeat = state.Repeat
                    });
                }
            }
            catch
            {
                // A failed save or send must not end the loop (or, from async void, the app).
            }
        }
    }

    private SessionState CaptureSessionState() => new()
    {
        Queue = new List<Track>(_queue.Queue),
        CurrentIndex = _queue.CurrentIndex,
        PositionSeconds = _playback.Position.TotalSeconds,
        DurationSeconds = _playback.Duration.TotalSeconds,
        Volume = _playback.Volume,
        IsPlaying = _playback.IsPlaying,
        Shuffle = _queue.Shuffle,
        Repeat = _queue.Repeat
    };

    /// <summary>
    /// The loop only saves every 10 s, so without this the last stretch of position and any
    /// queue edit since would be lost on exit. Closing can't await; block briefly on a save
    /// run on the thread pool, whose WinRT calls never need the UI thread, so no deadlock.
    /// </summary>
    private void SaveSessionOnExit()
    {
        if (!_isSessionLoopRunning || !_api.IsAuthenticated) return;
        try
        {
            var state = CaptureSessionState();
            System.Threading.Tasks.Task.Run(() => SessionStorageService.SaveAsync(state))
                .Wait(TimeSpan.FromSeconds(2));
        }
        catch { /* exiting anyway */ }
    }

    private void OnSyncStateReceived(SyncState state)
    {
        _uiQueue.TryEnqueue(() =>
        {
            // Simple logic: if the queue is different, replace it.
            // In a real 'Connect' app, we might ask the user or only sync if not playing.
            bool queueChanged = state.Queue.Count != _queue.Queue.Count || 
                               (state.Queue.Count > 0 && _queue.Queue.Count > 0 && state.Queue[0].Id != _queue.Queue[0].Id);

            if (queueChanged)
            {
                _queue.RestoreQueue(state.Queue, state.CurrentIndex, autoPlay: false);
            }
            else if (state.CurrentIndex != _queue.CurrentIndex)
            {
                _queue.SetCurrentIndex(state.CurrentIndex);
            }

            _queue.SetModes(state.Shuffle, state.Repeat);

            // Update playback
            if (state.IsPlaying && !_playback.IsPlaying)
            {
                // Note: We might need to seek before playing
                var pos = TimeSpan.FromSeconds(state.PositionSeconds);
                _playback.Player.PlaybackSession.Position = pos;
                _playback.Play();
            }
            else if (!state.IsPlaying && _playback.IsPlaying)
            {
                _playback.Pause();
            }
            
            // Sync position if it's more than 3 seconds apart
            if (Math.Abs(state.PositionSeconds - _playback.Position.TotalSeconds) > 3)
            {
                _playback.Player.PlaybackSession.Position = TimeSpan.FromSeconds(state.PositionSeconds);
            }

            _playback.Volume = state.Volume;
        });
    }

    private void OnSyncCommandReceived(SyncCommand cmd)
    {
        _uiQueue.TryEnqueue(() =>
        {
            switch (cmd.Type)
            {
                case "PLAY": _playback.Play(); break;
                case "PAUSE": _playback.Pause(); break;
                case "NEXT": _queue.Next(); break;
                case "PREV": _queue.Previous(); break;
                case "TRANSFER":
                    // Start playing if not already; the SyncState update will set the track/pos
                    if (!_playback.IsPlaying) _playback.Play();
                    break;
                case "GOTO":
                    if (int.TryParse(cmd.Value, out var index))
                        _queue.SetCurrentIndex(index);
                    break;
                case "SEEK": 
                    if (double.TryParse(cmd.Value, out var sec)) 
                        _playback.Player.PlaybackSession.Position = TimeSpan.FromSeconds(sec); 
                    break;
                case "VOLUME":
                    if (double.TryParse(cmd.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var vol))
                        _playback.Volume = vol;
                    break;
            }
        });
    }

    // ?? Navigation ??

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
#if DEBUG
        var debugItem = new NavigationViewItem
        {
            Content = "Debug",
            Tag = "debug",
            Icon = new FontIcon { Glyph = "\uE756" },
        };
        NavView.FooterMenuItems.Add(debugItem);
#endif
        NavView.SelectedItem = NavView.MenuItems[0];
        // The selection of the first item on load will trigger SelectionChanged, but we removed navigation from it.
        // So we manually navigate to Home.
        NavigateTo(typeof(HomePage), null);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // We handle navigation in NavView_ItemInvoked to support re-clicking the selected item.
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            NavigateTo(typeof(SettingsPage), null);
            return;
        }

        if (args.InvokedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();

            if (tag is not null && tag.StartsWith("playlist:"))
            {
                var uuid = tag["playlist:".Length..];
                NavigateTo(typeof(PlaylistDetailPage), uuid);
                return;
            }

            Type? pageType = tag switch
            {
                "home" => typeof(HomePage),
                "library" => typeof(LibraryPage),
#if DEBUG
                "debug" => typeof(DebugPage),
#endif
                _ => null
            };

            if (pageType is not null)
                NavigateTo(pageType, null);
        }
    }

    internal void NavigateTo(Type pageType, object? parameter)
    {
        // If we are already on this type of page and there's no parameter (i.e. root page clicked again), 
        // we can clear the backstack to basically reset it to "Home Home", or we can just navigate 
        // to it and clear the backstack.
        
        // Always navigate to ensure the content refreshes or we jump to the top.
        if (ContentFrame.CurrentSourcePageType != pageType || parameter != null)
        {
            ContentFrame.Navigate(pageType, parameter);
        }
        else
        {
            // If already on the page (e.g. Home), clear its backstack if it has one
            ContentFrame.BackStack.Clear();
            PageHeader.CanGoBack = false;
        }
        
        // If we navigated to a root level navigation item (not an artist/album), we could clear the backstack.
        if (parameter == null && (pageType == typeof(HomePage) || pageType == typeof(LibraryPage) || pageType == typeof(SettingsPage)))
        {
            ContentFrame.BackStack.Clear();
            PageHeader.CanGoBack = false;
        }
    }

    // ?? Back / Forward ??

    private void PageHeader_BackClick(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
    }

    private void PageHeader_ForwardClick(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoForward)
            ContentFrame.GoForward();
    }

    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        PageHeader.CloseSuggestions();
        PageHeader.CanGoBack = ContentFrame.CanGoBack;
        PageHeader.CanGoForward = ContentFrame.CanGoForward;
    }

    // ─── Header Search ──────────────────────────────────────────

    private void PageHeader_SearchSubmitted(object? sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is SearchSuggestion suggestion)
        {
            switch (suggestion.Type)
            {
                case "album":
                    ContentFrame.Navigate(typeof(Pages.PlaylistDetailPage), suggestion.Id);
                    break;
                case "artist":
                    ContentFrame.Navigate(typeof(Pages.ArtistPage), suggestion.Id);
                    break;
                case "track":
                    // Play track immediately
                    var track = new Models.Track
                    {
                        Id = suggestion.TrackId,
                        Title = suggestion.Name,
                        Artist = string.IsNullOrEmpty(suggestion.ArtistName)
                            ? _resourceLoader.GetString("Library_UnknownArtist") : suggestion.ArtistName,
                        CoverUrl = suggestion.CoverUrl,
                        IsExplicit = suggestion.IsExplicit,
                        AlbumId = suggestion.Id,
                        ArtistId = suggestion.ArtistId
                    };
                    _queue.PlayTrack(track);
                    break;
                case "search":
                    ContentFrame.Navigate(typeof(SearchResultsPage), suggestion.Name);
                    break;
            }
            PageHeader.ClearSearch();
        }
        else
        {
            var query = args.QueryText?.Trim();
            if (!string.IsNullOrEmpty(query) && _api.IsAuthenticated)
            {
                ContentFrame.Navigate(typeof(SearchResultsPage), query);
                PageHeader.ClearSearch();
            }
        }
    }

    // ?? Sidebar playlists ??

    private async void LoadSidebarPlaylists()
    {
        if (!_api.IsAuthenticated) return;

        try
        {
            var result = await _api.GetFavoritePlaylistsAsync(limit: 50);
            var playlists = result.Items
                .Where(i => i is not null)
                .Select(i => i!)
                .ToList();

            if (playlists.Count == 0) return;

            NavView.MenuItems.Add(new NavigationViewItemSeparator());
            NavView.MenuItems.Add(new NavigationViewItemHeader { Content = _resourceLoader.GetString("Sidebar_Playlists") });

            foreach (var p in playlists)
            {
                var navItem = new NavigationViewItem
                {
                    Tag = $"playlist:{p.Uuid}",
                    Icon = new FontIcon { Glyph = Glyphs.Playlist }
                };

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = p.Title,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
                });
                stack.Children.Add(new TextBlock
                {
                    Text = string.Format(_resourceLoader.GetString("PlaylistDetail_Tracks"), p.NumberOfTracks),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
                navItem.Content = stack;

                NavView.MenuItems.Add(navItem);
            }
        }
        catch { }
    }

    // ?? Playback ??



    private async void OnCurrentTrackChanged(Track? track)
    {
        _sessionDirty = true;

        // Scrobble the previous track on skip if threshold was met
        TryScrobbleCurrent();

        if (track is not null)
        {
            _uiQueue.TryEnqueue(() =>
            {
                Title = $"{track.Title} — {track.Artist} | Tid3";
                _trayIcon.SetToolTip($"{track.Title} — {track.Artist}");
            });

            // Track before loading: listening time only starts counting with PlaybackStarted,
            // which the load raises once audio plays (or the user does, for a paused load).
            ScrobbleBegin(track);

            await _playback.PlayTrackAsync(track.Id, track.CoverUrl, track.Title, track.Artist,
                autoPlay: _queue.IsAutoPlayEnabled, startPosition: _queue.StartPosition);

            // Skipping quickly starts loads that get cancelled. Only the track that is still
            // current may announce itself, or every skipped one would send a Last.fm update,
            // pop a notification and fetch lyrics.
            if (!ReferenceEquals(_queue.CurrentTrack, track)) return;

            _ = LastFmService.UpdateNowPlayingAsync(track.Title, track.Artist);

            // Trigger quality UI update manually after track info is loaded
            if (PlaybackBar != null)
            {
                _uiQueue.TryEnqueue(() => PlaybackBar.RefreshQuality());
            }

            // Refresh queue panel if open
            if (_queuePanelVisible)
                _uiQueue.TryEnqueue(() => QueuePanel.Refresh());

            // Load lyrics if panel is open
            if (_lyricsVisible)
                _ = LyricsPanel.LoadLyricsAsync(track);

            // Toast notification
            if (SettingsService.ShowNotifications)
                ShowTrackNotification(track);
        }
        else
        {
            // The queue was emptied: the audio of the removed track must not keep playing.
            _playback.Stop();
            _uiQueue.TryEnqueue(() =>
            {
                Title = "Tid3";
                _trayIcon.SetToolTip("Tid3");
                LyricsPanel.ClearLyrics();
            });
        }
    }



    private void OnPositionChanged(TimeSpan position, TimeSpan duration)
    {
        _uiQueue.TryEnqueue(() =>
        {
            // Sync lyrics
            if (_lyricsVisible)
                LyricsPanel.UpdateLyricsHighlight(position);
        });
    }

    // Raised on the UI thread by PlaybackService, so the queue is advanced synchronously:
    // deferring it again would let other queue events slip in between.
    private async void OnPlaybackEnded()
    {
        // Natural end always qualifies — scrobble unconditionally
        TryScrobbleCurrent(forceScrobble: true);
        _scrobbleTrack = null;

        if (_queue.AdvanceAfterTrackEnded()) return;
        if (!SettingsService.PlayRadioAfterQueue || _queue.CurrentTrack is not { } lastTrack) return;

        try
        {
            var result = await _api.GetTrackRadioAsync(lastTrack.Id, limit: 50);
            var index = 1;
            var tracks = result.Items
                .Where(t => t is not null && t.Id != lastTrack.Id)
                .Select(t => TrackMapper.Map(t, index++))
                .ToList();

            // Something else may have been started while the radio was loading.
            if (tracks.Count == 0 || !ReferenceEquals(_queue.CurrentTrack, lastTrack)) return;

            // Append rather than replace, so the queue panel keeps what was played before.
            _queue.AddRange(tracks);
            _queue.Next();
        }
        catch { }
    }

    private void OnPlaybackFailed(PlaybackFailure failure)
    {
        // Already moved on (a skip, a new queue) since this track failed.
        if (_queue.CurrentTrack is not { } track || track.Id != failure.TrackId) return;

        // A paused load that failed, such as the restored session opening offline, must not
        // start skipping and playing on its own. Play retries it.
        if (!failure.WasPlaying) return;

        if (failure.ConsecutiveFailures >= MaxConsecutivePlaybackFailures)
        {
            ShowPlaybackMessage(_resourceLoader.GetString("Playback_RepeatedFailures"));
            return;
        }

        ShowPlaybackMessage(string.Format(_resourceLoader.GetString(
            _queue.Next() ? "Playback_TrackSkipped" : "Playback_TrackFailed"), track.Title));
    }

    private void ShowPlaybackMessage(string message)
    {
        if (_playbackMessageTimer is null)
        {
            _playbackMessageTimer = _uiQueue.CreateTimer();
            _playbackMessageTimer.Interval = TimeSpan.FromSeconds(6);
            _playbackMessageTimer.IsRepeating = false;
            _playbackMessageTimer.Tick += (_, _) => PlaybackInfoBar.IsOpen = false;
        }

        PlaybackInfoBar.Message = message;
        PlaybackInfoBar.IsOpen = true;
        _playbackMessageTimer.Stop();
        _playbackMessageTimer.Start();
    }



    // ?? Queue flyout ??

    private void QueueCloseButton_Click(object sender, EventArgs e)
    {
        if (_queuePanelVisible)
            PlaybackBar_QueueButtonClicked(sender, EventArgs.Empty);
    }

    private void PlaybackBar_QueueButtonClicked(object sender, EventArgs e)
    {
        _queuePanelVisible = !_queuePanelVisible;
        if (_queuePanelVisible && _lyricsVisible)
        {
            _lyricsVisible = false;
            LyricsPanel.Visibility = Visibility.Collapsed;
            if (PlaybackBar != null) PlaybackBar.IsLyricsActive = false;
        }

        QueuePanel.Visibility = _queuePanelVisible ? Visibility.Visible : Visibility.Collapsed;
        LyricsColumn.Width = (_queuePanelVisible || _lyricsVisible) ? new GridLength(360) : new GridLength(0);

        if (PlaybackBar != null) PlaybackBar.IsQueueActive = _queuePanelVisible;

        if (_queuePanelVisible)
        {
            QueuePanel.Refresh();
            QueuePanel.ScrollToCurrent();
        }
    }



    // Removed QualityButton_Click

    // ?? Helpers ??



    // ═══════════ Lyrics ═══════════

    private void LyricsCloseButton_Click(object sender, EventArgs e)
    {
        PlaybackBar_LyricsButtonClicked(sender, EventArgs.Empty);
    }

    private void PlaybackBar_LyricsButtonClicked(object sender, EventArgs e)
    {
        _lyricsVisible = !_lyricsVisible;
        if (_lyricsVisible && _queuePanelVisible)
        {
            _queuePanelVisible = false;
            QueuePanel.Visibility = Visibility.Collapsed;
            if (PlaybackBar != null) PlaybackBar.IsQueueActive = false;
        }

        LyricsPanel.Visibility = _lyricsVisible ? Visibility.Visible : Visibility.Collapsed;
        LyricsColumn.Width = (_queuePanelVisible || _lyricsVisible) ? new GridLength(360) : new GridLength(0);

        if (PlaybackBar != null) PlaybackBar.IsLyricsActive = _lyricsVisible;

        if (_lyricsVisible && _queue.CurrentTrack is not null)
        {
            _ = LyricsPanel.LoadLyricsAsync(_queue.CurrentTrack);
        }
    }

    // ── Last.fm scrobble tracking ────────────────────────────────────────────

    private void ScrobbleBegin(Track track)
    {
        _scrobbleTrack = track;
        _scrobbleStartTime = DateTimeOffset.UtcNow;
        _scrobbleListenedTime = TimeSpan.Zero;
        // Counting starts with ScrobbleResume. Starting it here counted the time a restored,
        // paused session sat idle as listening.
        _scrobbleIsPlaying = false;
    }

    private void ScrobblePause()
    {
        if (!_scrobbleIsPlaying) return;
        _scrobbleListenedTime += DateTimeOffset.UtcNow - _scrobbleStartTime;
        _scrobbleIsPlaying = false;
    }

    private void ScrobbleResume()
    {
        if (_scrobbleIsPlaying || _scrobbleTrack is null) return;
        _scrobbleStartTime = DateTimeOffset.UtcNow;
        _scrobbleIsPlaying = true;
    }

    /// <summary>
    /// Scrobbles the current track if the Last.fm threshold has been met:
    /// listened ≥ 50% of duration, or ≥ 4 minutes (whichever is less),
    /// and the track is longer than 30 seconds.
    /// forceScrobble bypasses the threshold (used on natural track end).
    /// </summary>
    private void TryScrobbleCurrent(bool forceScrobble = false)
    {
        if (_scrobbleTrack is null) return;

        // Flush any currently-playing time
        var listened = _scrobbleListenedTime;
        if (_scrobbleIsPlaying)
            listened += DateTimeOffset.UtcNow - _scrobbleStartTime;

        // The track's own length. The player's duration may already be the next track's: after
        // a gapless transition it has moved on before this runs, and a short track followed by
        // a long one then missed the threshold and was never scrobbled.
        var duration = _scrobbleTrack.DurationSeconds > 0
            ? TimeSpan.FromSeconds(_scrobbleTrack.DurationSeconds)
            : _playback.Duration;

        // Last.fm rules: track must be > 30s
        if (!forceScrobble && duration.TotalSeconds < 30) return;

        // Threshold: 50% of duration OR 4 minutes, whichever is smaller
        var threshold = duration.TotalSeconds > 0
            ? TimeSpan.FromSeconds(Math.Min(duration.TotalSeconds * 0.5, 240))
            : TimeSpan.FromMinutes(4);

        if (forceScrobble || listened >= threshold)
        {
            // Timestamp = when the track started playing
            var startTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)listened.TotalSeconds;
            _ = LastFmService.ScrobbleAsync(_scrobbleTrack.Title, _scrobbleTrack.Artist, startTimestamp);
        }

        _scrobbleTrack = null;
        _scrobbleIsPlaying = false;
        _scrobbleListenedTime = TimeSpan.Zero;
    }

    private void ShowTrackNotification(Track track)
    {
        if (!AppNotificationManager.IsSupported()) return;
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(track.Title)
                .AddText(track.Artist)
                .SetDuration(AppNotificationDuration.Default);

            if (!string.IsNullOrEmpty(track.CoverUrl))
                builder.SetAppLogoOverride(new Uri(track.CoverUrl));

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch { }
    }

    public void ApplyTheme()
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = SettingsService.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    public void ShowTrayIcon() => _trayIcon.Show(this);
    public void HideTrayIcon() => _trayIcon.Remove();

    /// <summary>
    /// Brings the window back when Tid3 is launched again while running (see Program):
    /// out of the tray, out of the taskbar if minimized, and to the front.
    /// </summary>
    public void BringToFront()
    {
        AppWindow.Show(true);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
            { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
    }

}
