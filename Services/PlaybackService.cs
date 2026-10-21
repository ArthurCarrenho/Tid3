using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage.Streams;
using TidalUi3.Helpers;
using TidalUi3.Models;

namespace TidalUi3.Services;

/// <summary>A track that could not be played, see <see cref="PlaybackService.PlaybackFailed"/>.</summary>
/// <param name="WasPlaying">Whether playback was wanted at the time, as opposed to a track
/// loaded paused, like the one a restored session opens with.</param>
/// <param name="ConsecutiveFailures">Failures since a track last opened successfully.</param>
public readonly record struct PlaybackFailure(int TrackId, bool WasPlaying, int ConsecutiveFailures);

/// <summary>An audio output to play through. The system default has an empty id.</summary>
public sealed record AudioOutputDevice(string Id, string Name)
{
    // What lists and screen readers show for the item.
    public override string ToString() => Name;
}

public sealed class PlaybackService : IDisposable
{
    // Pressing "previous" this far into a track restarts it instead of going back one.
    private static readonly TimeSpan PreviousRestartThreshold = TimeSpan.FromSeconds(3);

    private readonly TidalApiClient _api;
    private readonly MediaPlayer _player;

    public MediaPlayer Player => _player;

    public TimeSpan Position => _player.PlaybackSession.Position;
    public TimeSpan Duration
    {
        get
        {
            var session = _player.PlaybackSession;
            if (session == null) return TimeSpan.Zero;
            var d = session.NaturalDuration;
            // DASH often reports 1s or 0s initially. If it's less than 5s,
            // it's likely a stream initialization artifact.
            if (d.TotalSeconds > 5) return d;

            var currentTrack = App.Queue.CurrentTrack;
            if (currentTrack != null && currentTrack.DurationSeconds > 0)
                return TimeSpan.FromSeconds(currentTrack.DurationSeconds);

            return d;
        }
    }
    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public string CurrentQuality { get; private set; } = "";
    public TidalPlaybackInfo? CurrentPlaybackInfo { get; private set; }
    public TidalManifest? CurrentManifest { get; private set; }

    public event Action? PlaybackStarted;
    public event Action? PlaybackPaused;
    /// <summary>Raised on the UI thread when the current track played to its end.</summary>
    public event Action? PlaybackEnded;
    public event Action<TimeSpan, TimeSpan>? PositionChanged;
    /// <summary>
    /// Raised on the UI thread when the current track can't be played: its stream couldn't be
    /// fetched or opened, or it broke off and fetching a fresh one didn't help. The handler
    /// decides whether to skip; <see cref="Play"/> retries the track.
    /// </summary>
    public event Action<PlaybackFailure>? PlaybackFailed;

    /// <summary>
    /// True while playback was asked for but no audio is coming out yet: the track's stream
    /// is being fetched, or the player is opening or buffering it.
    /// </summary>
    public bool IsBuffering { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="IsBuffering"/> changes.</summary>
    public event Action<bool>? BufferingChanged;

    public event Action? SmtcNext;
    public event Action? SmtcPrevious;

    private System.Threading.Timer? _positionTimer;
    private bool _isSeeking;
    private bool _isNativeGaplessTransition;

    // ── Gapless preload state (UI thread only) ──
    // The queue entry whose audio is being fetched, or already sits in _playbackList behind
    // the current item. Compared by reference, so two copies of one track stay distinct.
    private Track? _preloadTarget;
    // The list item holding that audio once the fetch finished; null while in flight.
    private MediaPlaybackItem? _preloadedItem;
    private TidalPlaybackInfo? _preloadedPlaybackInfo;
    private TidalManifest? _preloadedManifest;
    private string _preloadedQuality = "";
    private bool _preloadRevalidationPending;
    // False while PlayTrackAsync is still building the list for a new track: a preload
    // started then would land in the list that is about to be thrown away.
    private bool _isTrackReady;
    // Bumped whenever what is playing changes under our control. End-of-track events
    // capture it when they fire and are dropped if it moved on before they were handled,
    // so a single track end can never advance the queue twice.
    private int _playbackGeneration;
    private CancellationTokenSource? _preloadCts;
    private CancellationTokenSource? _playTrackCts;

    // ── Failure handling state (UI thread only, except the position) ──
    private MediaPlaybackItem? _currentItem;
    // Whether _currentItem opened once. A track that played and then fails, typically because
    // its stream URL expired during a long pause, is worth reloading; one that never opened is not.
    private bool _currentItemOpened;
    private int _currentTrackId;
    // PlayTrackAsync is still fetching the stream (playback info, manifest).
    private bool _isFetchingTrack;
    // The current track could not be loaded, so Play has to load it again.
    private bool _loadFailed;
    private bool _wantsToPlay;
    private int _consecutiveFailures;
    // Last position seen while playing, written by the position timer's thread. The player
    // reports 0 once the stream fails, so a reload resumes from here.
    private long _lastPositionTicks;

    private TimeSpan LastPosition => TimeSpan.FromTicks(Interlocked.Read(ref _lastPositionTicks));

    public PlaybackService(TidalApiClient api)
    {
        _api = api;
        _player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
        };

        _playbackList = new MediaPlaybackList();
        _playbackList.MaxPlayedItemsToKeepOpen = 1;
        _player.Source = _playbackList;
        Subscribe(_playbackList);

        // The gapless preload caches audio for whichever track comes next. Editing the queue
        // or changing repeat can change that, so re-check the preload after either.
        App.Queue.Queue.CollectionChanged += OnQueueCollectionChanged;
        App.Queue.ModesChanged += ScheduleGaplessRevalidation;

        // CommandManager must be enabled for the Windows media overlay notification
        _player.CommandManager.IsEnabled = true;
        _player.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        // Media keys and the Windows overlay arrive on a background thread. Go through Play and
        // Pause on the UI thread like the in-app buttons, so a failed track is retried and
        // the PlaybackStarted/Paused handlers never touch the UI or the queue off-thread.
        _player.CommandManager.PlayReceived += (_, args) =>
        {
            args.Handled = true;
            App.MainWindow?.DispatcherQueue.TryEnqueue(Play);
        };
        _player.CommandManager.PauseReceived += (_, args) =>
        {
            args.Handled = true;
            App.MainWindow?.DispatcherQueue.TryEnqueue(Pause);
        };
        _player.CommandManager.NextReceived += (_, _) => SmtcNext?.Invoke();
        _player.CommandManager.PreviousReceived += (_, _) => SmtcPrevious?.Invoke();
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.Volume = 0.75;

        _ = ApplyOutputDeviceAsync();
        StartOutputWatcher();
    }

    public async Task PlayTrackAsync(int trackId, string? coverUrl = null,
        string? title = null, string? artist = null, bool autoPlay = true,
        TimeSpan startPosition = default)
    {
        Trace($"ENTER PlayTrackAsync track={trackId} autoPlay={autoPlay} gaplessFlag={_isNativeGaplessTransition}");
        _currentTrackId = trackId;
        if (_isNativeGaplessTransition)
        {
            CurrentPlaybackInfo = _preloadedPlaybackInfo;
            CurrentManifest = _preloadedManifest;
            CurrentQuality = _preloadedQuality;
            Trace($"  -> adotou preload (track={trackId}), retorno antecipado");
            return;
        }

        // Cancel any previous PlayTrackAsync still awaiting (rapid skipping)
        _playTrackCts?.Cancel();
        _playTrackCts?.Dispose();
        var playCts = new CancellationTokenSource();
        _playTrackCts = playCts;
        var playCt = playCts.Token;

        _loadFailed = false;
        _wantsToPlay = autoPlay;
        _currentItem = null;
        _currentItemOpened = false;
        Interlocked.Exchange(ref _lastPositionTicks, startPosition.Ticks);
        _isFetchingTrack = true;
        UpdateBuffering();

        try
        {
            // Whatever was preloaded behind the old track is irrelevant now, and any
            // end-of-track event still on its way belongs to the old track.
            InvalidateGaplessPreload();
            _isTrackReady = false;
            Interlocked.Increment(ref _playbackGeneration);

            // Stop the previous track immediately. The list is rebuilt further down instead
            // of being cleared: emptying a MediaPlaybackList while it is the player's
            // source detaches the two permanently -- CurrentItem stays null, MoveTo fails,
            // and every later Play() is a no-op, so tracks load but never start.
            _player.Pause();

            Trace($"  lista limpa, buscando info track={trackId}");
            var playbackInfo = await _api.GetPlaybackInfoAsync(trackId, quality: SettingsService.AudioQuality, ct: playCt);
            var mediaMetadata = await _api.GetTrackAsync(trackId, ct: playCt);
            Trace($"  info obtida track={trackId} mime={playbackInfo.ManifestMimeType}");
            var decodedManifest = System.Text.Encoding.UTF8.GetString(
                System.Convert.FromBase64String(playbackInfo.Manifest));

            playCt.ThrowIfCancellationRequested();

            CurrentPlaybackInfo = playbackInfo;
            CurrentManifest = null;

            MediaSource source;

            // Handle DASH manifests natively for Hi-Res FLAC
            if (playbackInfo.ManifestMimeType == "application/dash+xml" || decodedManifest.TrimStart().StartsWith("<"))
            {
                source = await CreateDashSourceAsync(decodedManifest, playCt);
                CurrentQuality = QualityHelper.FormatQuality(mediaMetadata);
            }
            else
            {
                // Regular JSON manifest with direct URLs
                var manifest = TidalApiClient.DecodeManifest(playbackInfo);
                CurrentManifest = manifest;
                if (manifest.Urls is not { Count: > 0 })
                    throw new InvalidOperationException($"Playback manifest for track {trackId} has no stream URL.");
                source = MediaSource.CreateFromUri(new Uri(manifest.Urls[0]));
                CurrentQuality = QualityHelper.FormatQuality(mediaMetadata);
            }

            playCt.ThrowIfCancellationRequested();

            // Use MediaPlaybackItem with display properties for SMTC overlay
            var playbackItem = startPosition > TimeSpan.Zero
                ? new MediaPlaybackItem(source, startPosition)
                : new MediaPlaybackItem(source);
            var displayProps = playbackItem.GetDisplayProperties();
            displayProps.Type = MediaPlaybackType.Music;
            displayProps.MusicProperties.Title = title ?? "";
            displayProps.MusicProperties.Artist = artist ?? "";
            if (!string.IsNullOrEmpty(coverUrl))
                displayProps.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(coverUrl));
            playbackItem.ApplyDisplayProperties(displayProps);

            ResetPlaybackList(playbackItem);
            _currentItem = playbackItem;
            _isTrackReady = true;
            Trace($"  lista reconstruida track={trackId} itens={_playbackList.Items.Count} currentNull={_playbackList.CurrentItem is null} idx={_playbackList.CurrentItemIndex} state={_player.PlaybackSession.PlaybackState}");

            SyncGaplessPreload();

            // Play/Pause pressed while loading changed _wantsToPlay; that wins over autoPlay.
            if (_wantsToPlay)
            {
                Trace($"  Play() track={trackId}");
                _player.Play();
                Trace($"  apos Play(): state={_player.PlaybackSession.PlaybackState}");
                StartPositionTimer();
                PlaybackStarted?.Invoke();
            }
            else
            {
                _player.Pause();
                PlaybackPaused?.Invoke();
                // The position timer only runs while playing, so publish the starting point once.
                PositionChanged?.Invoke(startPosition, Duration);
            }
        }
        // Only a newer track cancels playCt. Any other cancellation is an HTTP timeout, which is
        // a failure like any other and goes to the handler below.
        catch (OperationCanceledException) when (playCt.IsCancellationRequested) { Trace($"  CANCELADO track={trackId}"); }
        catch (Exception ex)
        {
            Trace($"  EXCECAO track={trackId}: {ex.GetType().Name}: {ex.Message}");
            AppLog.Exception("PlaybackError", ex);

            // A newer track took over meanwhile; this failure no longer matters.
            if (playCt.IsCancellationRequested) return;
            CurrentQuality = "";
            ReportFailure(trackId, _wantsToPlay);
        }
        finally
        {
            // A load superseded by a newer one finishing late must not clear the newer's flag.
            if (ReferenceEquals(_playTrackCts, playCts))
            {
                _isFetchingTrack = false;
                UpdateBuffering();
            }
        }
    }

    private static async Task<MediaSource> CreateDashSourceAsync(string dashXml, CancellationToken ct)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteString(dashXml);
            await writer.StoreAsync().AsTask(ct);
            await writer.FlushAsync().AsTask(ct);
            writer.DetachStream();
        }
        stream.Seek(0);

        var result = await AdaptiveMediaSource.CreateFromStreamAsync(
            stream,
            new Uri("https://sp-ad-cf.audio.tidal.com/"),
            "application/dash+xml");

        if (result.Status != AdaptiveMediaSourceCreationStatus.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create DASH source: {result.Status} - {result.ExtendedError?.Message}");
        }

        return MediaSource.CreateFromAdaptiveMediaSource(result.MediaSource);
    }

    public void Play()
    {
        if (App.Queue.CurrentTrack is not { } track) return;

        // The track never loaded (offline, or its stream failed): pressing play should try
        // again rather than do nothing.
        if (_loadFailed)
        {
            _ = PlayTrackAsync(track.Id, track.CoverUrl, track.Title, track.Artist,
                autoPlay: true, startPosition: LastPosition);
            return;
        }

        _wantsToPlay = true;
        UpdateBuffering();

        // Still loading: the player holds the previous track until the new audio is in, and
        // playing it now would resume that one. PlayTrackAsync starts playback when done.
        if (!_isTrackReady)
        {
            PlaybackStarted?.Invoke();
            return;
        }

        _player.Play();
        StartPositionTimer();
        PlaybackStarted?.Invoke();
    }

    public void Pause()
    {
        _wantsToPlay = false;
        // Only while the current track is the one loaded: during a load the player still holds
        // the previous track, and its position would become the retry point of the new one.
        if (_isTrackReady)
            Interlocked.Exchange(ref _lastPositionTicks, Position.Ticks);
        _player.Pause();
        StopPositionTimer();
        UpdateBuffering();
        PlaybackPaused?.Invoke();
    }

    /// <summary>Stops playback for good, e.g. when the queue was cleared.</summary>
    public void Stop()
    {
        _playTrackCts?.Cancel();
        InvalidateGaplessPreload();
        _isTrackReady = false;
        _isFetchingTrack = false;
        _loadFailed = false;
        _wantsToPlay = false;
        _currentItem = null;
        Interlocked.Increment(ref _playbackGeneration);
        UpdateBuffering();

        _player.Pause();
        // Detach the old audio so the media keys can't resume a track that left the queue.
        // The next PlayTrackAsync attaches a fresh list.
        _player.Source = null;
        StopPositionTimer();
        CurrentQuality = "";
        PlaybackPaused?.Invoke();
    }

    public void TogglePlayPause()
    {
        // While a track loads the player isn't playing yet, but the button already shows
        // pause; pressing it then should cancel the pending start.
        if (IsPlaying || (_wantsToPlay && !_isTrackReady && !_loadFailed))
            Pause();
        else
            Play();
    }

    /// <summary>
    /// "Previous" as music players do it: restart the track once it has been playing for a
    /// few seconds, otherwise go back one. At the start of the queue it just restarts.
    /// </summary>
    public void SkipPrevious()
    {
        if (Position > PreviousRestartThreshold || !App.Queue.Previous())
            Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan position)
    {
        _player.PlaybackSession.Position = position;
    }

    public void BeginSeek() => _isSeeking = true;
    public void EndSeek(double sliderValue)
    {
        _isSeeking = false;
        var duration = Duration;
        if (duration.TotalSeconds > 0)
            Seek(TimeSpan.FromSeconds(sliderValue / 100.0 * duration.TotalSeconds));
    }

    public event Action<double>? VolumeChanged;

    private double _volume = 0.75;
    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (_volume != clamped)
            {
                _volume = clamped;
                // Human hearing is logarithmic. Use a power-law curve (squared)
                // so that 10% volume sounds like 10% loudness, not 50%.
                _player.Volume = Math.Pow(clamped, 2);
                VolumeChanged?.Invoke(clamped);
            }
        }
    }

    // ── Gapless & Internal ──

    private MediaPlaybackList _playbackList;


    /// <summary>
    /// Replaces the playback list with a fresh one containing <paramref name="firstItem"/>.
    /// Clearing the existing list is not an option: a MediaPlaybackList that goes empty
    /// while it is the player's source is dropped by the player and never re-attaches.
    /// </summary>
    private void ResetPlaybackList(MediaPlaybackItem firstItem)
    {
        Unsubscribe(_playbackList);

        _playbackList = new MediaPlaybackList { MaxPlayedItemsToKeepOpen = 1 };
        _playbackList.Items.Add(firstItem);
        Subscribe(_playbackList);

        _player.Source = _playbackList;
    }

    private void Subscribe(MediaPlaybackList list)
    {
        list.CurrentItemChanged += OnCurrentItemChanged;
        list.ItemOpened += OnItemOpened;
        list.ItemFailed += OnItemFailed;
    }

    private void Unsubscribe(MediaPlaybackList list)
    {
        list.CurrentItemChanged -= OnCurrentItemChanged;
        list.ItemOpened -= OnItemOpened;
        list.ItemFailed -= OnItemFailed;
    }

    /// <summary>
    /// Makes the preload match the track the queue will move to when the current one ends:
    /// keeps it when it still does, otherwise drops it and fetches the right one. Covers
    /// fetches still in flight too, so an edit made mid-preload is never missed.
    /// </summary>
    private void SyncGaplessPreload()
    {
        var queue = App.Queue;
        var nextIndex = queue.PeekAutoNextIndex();

        // Repeat-one replays through a normal reload, so there is nothing to preload then.
        var target = SettingsService.GaplessPlayback && _isTrackReady
            && nextIndex >= 0 && nextIndex != queue.CurrentIndex
                ? queue.Queue[nextIndex] : null;

        if (ReferenceEquals(target, _preloadTarget)) return;

        Trace($"SyncGaplessPreload: preload {_preloadTarget?.Id} -> {target?.Id}");
        InvalidateGaplessPreload();
        if (target is null) return;

        _preloadTarget = target;
        _ = PreloadNextTrackAsync(target, _preloadCts!.Token);
    }

    private async Task PreloadNextTrackAsync(Track target, CancellationToken ct)
    {
        try
        {
            var playbackInfo = await _api.GetPlaybackInfoAsync(target.Id, quality: SettingsService.AudioQuality, ct: ct);
            var mediaMetadata = await _api.GetTrackAsync(target.Id, ct: ct);
            var decodedManifest = System.Text.Encoding.UTF8.GetString(
                System.Convert.FromBase64String(playbackInfo.Manifest));

            MediaSource source;
            TidalManifest? manifest = null;
            if (playbackInfo.ManifestMimeType == "application/dash+xml" || decodedManifest.TrimStart().StartsWith("<"))
            {
                source = await CreateDashSourceAsync(decodedManifest, ct);
            }
            else
            {
                manifest = TidalApiClient.DecodeManifest(playbackInfo);
                if (manifest.Urls is not { Count: > 0 }) return;
                source = MediaSource.CreateFromUri(new Uri(manifest.Urls[0]));
            }

            var playbackItem = new MediaPlaybackItem(source);
            var displayProps = playbackItem.GetDisplayProperties();
            displayProps.Type = MediaPlaybackType.Music;
            displayProps.MusicProperties.Title = target.Title;
            displayProps.MusicProperties.Artist = target.Artist;
            if (!string.IsNullOrEmpty(target.CoverUrl))
                displayProps.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(target.CoverUrl));
            playbackItem.ApplyDisplayProperties(displayProps);

            // The queue may have moved on while this was downloading. Only the preload that
            // is still wanted may touch the list.
            if (ct.IsCancellationRequested || !ReferenceEquals(_preloadTarget, target)) return;

            _playbackList.Items.Add(playbackItem);
            _preloadedItem = playbackItem;
            _preloadedPlaybackInfo = playbackInfo;
            _preloadedManifest = manifest;
            _preloadedQuality = QualityHelper.FormatQuality(mediaMetadata);
            Trace($"  preload pronto track={target.Id} itens={_playbackList.Items.Count}");
        }
        catch (OperationCanceledException) { /* superseded by a skip or a queue edit */ }
        catch (Exception ex)
        {
            AppLog.Exception("GaplessPreloadError", ex);
        }
    }

    private void OnQueueCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => ScheduleGaplessRevalidation();

    private void ScheduleGaplessRevalidation()
    {
        if (_preloadRevalidationPending) return;

        // QueueService mutates the collection first and fixes CurrentIndex afterwards, so
        // reading the index from inside this event would see a torn state. Deferring also
        // collapses multi-step edits (a move is a remove plus an insert) into one check.
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher is null) return;

        _preloadRevalidationPending = true;
        var enqueued = dispatcher.TryEnqueue(() =>
        {
            _preloadRevalidationPending = false;
            SyncGaplessPreload();
        });
        if (!enqueued) _preloadRevalidationPending = false;
    }

    // NOTE: do not dispose the MediaSource of items removed from _playbackList.
    // Analyzers flag the sources as leaked (CA2000), but MediaPlaybackList and the player
    // keep using them while the pipeline tears down asynchronously; disposing them here
    // blocks the calling thread and freezes the app on the next track change. The player
    // releases them when the items are collected. Left deliberately undisposed.
    // Drops the preloaded audio and everything queued behind the item now playing.
    private void InvalidateGaplessPreload()
    {
        _preloadCts?.Cancel();
        _preloadCts?.Dispose();
        _preloadCts = new CancellationTokenSource();

        _preloadTarget = null;
        _preloadedItem = null;
        _preloadedPlaybackInfo = null;
        _preloadedManifest = null;
        _preloadedQuality = "";

        // CurrentItemIndex is uint.MaxValue when no item is current; only trim when the
        // index is real, otherwise PlayTrackAsync clears the list anyway.
        var currentIndex = _playbackList.CurrentItemIndex;
        if (currentIndex >= (uint)_playbackList.Items.Count) return;

        for (var i = _playbackList.Items.Count - 1; i > (int)currentIndex; i--)
            _playbackList.Items.RemoveAt(i);
    }

    private void OnCurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
    {
        if (args.Reason != MediaPlaybackItemChangedReason.EndOfStream || args.NewItem is null) return;

        var newItem = args.NewItem;
        var generation = Volatile.Read(ref _playbackGeneration);

        // All queue and CTS access must happen on the UI thread.
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            // From a list or a track that has been replaced since: not ours to act on.
            if (sender != _playbackList || generation != _playbackGeneration) return;
            Interlocked.Increment(ref _playbackGeneration);

            var queue = App.Queue;
            var nextIndex = queue.PeekAutoNextIndex();
            var preloadIsNext = newItem == _preloadedItem && _preloadTarget is not null
                && nextIndex >= 0 && nextIndex != queue.CurrentIndex
                && ReferenceEquals(queue.Queue[nextIndex], _preloadTarget);

            Trace($"OnCurrentItemChanged EndOfStream: idx={queue.CurrentIndex} next={nextIndex} preload={_preloadTarget?.Id} adopt={preloadIsNext}");

            if (!preloadIsNext)
            {
                // The player rolled into audio the queue no longer wants: it was preloaded
                // before an edit that the revalidation had not caught up with yet. Stop it
                // and let the normal end-of-track path load whatever really comes next.
                _player.Pause();
                StopPositionTimer();
                PlaybackEnded?.Invoke();
                return;
            }

            // The player already moved on to the preloaded item; point the queue at it.
            _currentItem = newItem;
            _currentItemOpened = true;
            Interlocked.Exchange(ref _lastPositionTicks, 0);
            _isNativeGaplessTransition = true;
            queue.SetCurrentIndex(nextIndex);
            _isNativeGaplessTransition = false;

            // That item is playing now, so nothing is preloaded behind it any more.
            InvalidateGaplessPreload();

            // Remove already-played items so the list doesn't grow unboundedly.
            var playedCount = (int)_playbackList.CurrentItemIndex;
            for (var i = 0; i < playedCount; i++)
                _playbackList.Items.RemoveAt(0);

            PlaybackStarted?.Invoke();
            SyncGaplessPreload();
        });
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        // For Gapless playback, MediaEnded doesn't fire when the list moves on to a
        // preloaded item; it only fires once the list runs dry.
        var generation = Volatile.Read(ref _playbackGeneration);
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _playbackGeneration) return;
            Interlocked.Increment(ref _playbackGeneration);

            StopPositionTimer();
            PlaybackEnded?.Invoke();
        });
    }

    // ── Failures ──

    private void OnItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args)
    {
        var item = args.Item;
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (sender != _playbackList) return;
            _consecutiveFailures = 0;
            if (item == _currentItem) _currentItemOpened = true;
        });
    }

    private void OnItemFailed(MediaPlaybackList sender, MediaPlaybackItemFailedEventArgs args)
    {
        var item = args.Item;
        var generation = Volatile.Read(ref _playbackGeneration);
        AppLog.Exception("PlaybackItemFailed",
            $"{args.Error.ErrorCode}: {args.Error.ExtendedError?.Message} (preloaded={item == _preloadedItem})");

        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (sender != _playbackList || generation != _playbackGeneration) return;

            if (item == _preloadedItem)
                DropFailedPreload(item);
            else if (item == _currentItem)
                HandleCurrentItemFailure();
        });
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var generation = Volatile.Read(ref _playbackGeneration);
        AppLog.Exception("MediaFailed", $"{args.Error}: {args.ErrorMessage} {args.ExtendedErrorCode?.Message}");

        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            // The list reports its items through ItemFailed. Only act here if the current
            // track really stopped, not because something behind it broke.
            if (generation != _playbackGeneration || _currentItem is null
                || _playbackList.CurrentItem != _currentItem
                || _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) return;
            HandleCurrentItemFailure();
        });
    }

    /// <summary>
    /// The next track's audio turned out to be unusable. Take it out of the list so the player
    /// doesn't roll into it; when the current track ends, that track is then loaded the normal
    /// way, which reports the error properly. _preloadTarget is kept so the same broken
    /// preload isn't fetched again and again.
    /// </summary>
    private void DropFailedPreload(MediaPlaybackItem item)
    {
        _playbackList.Items.Remove(item);
        _preloadedItem = null;
        _preloadedPlaybackInfo = null;
        _preloadedManifest = null;
        _preloadedQuality = "";
    }

    private void HandleCurrentItemFailure()
    {
        // MediaFailed and ItemFailed can both report the same failure; only one may act.
        Interlocked.Increment(ref _playbackGeneration);
        _player.Pause();

        var track = App.Queue.CurrentTrack;
        var wasPlaying = _wantsToPlay;

        // It played fine before, so the track itself is OK: most likely its stream URL expired
        // during a long pause. Fetch a fresh one and carry on from the same spot. A reload that
        // fails before opening lands in the branch below, so this can't loop.
        if (_currentItemOpened && track is not null && track.Id == _currentTrackId)
        {
            Trace($"Item falhou depois de abrir, recarregando track={track.Id} pos={LastPosition}");
            _ = PlayTrackAsync(track.Id, track.CoverUrl, track.Title, track.Artist,
                autoPlay: wasPlaying, startPosition: LastPosition);
            return;
        }

        _isTrackReady = false;
        InvalidateGaplessPreload();
        ReportFailure(_currentTrackId, wasPlaying);
    }

    private void ReportFailure(int trackId, bool wasPlaying)
    {
        _loadFailed = true;
        _wantsToPlay = false;
        _consecutiveFailures++;
        StopPositionTimer();
        UpdateBuffering();
        PlaybackPaused?.Invoke();

        // Deferred: this can run inside a queue change (a load that fails at once), and a
        // handler that skips would then change the queue again from within that change.
        var failure = new PlaybackFailure(trackId, wasPlaying, _consecutiveFailures);
        App.MainWindow?.DispatcherQueue.TryEnqueue(() => PlaybackFailed?.Invoke(failure));
    }

    // ── Buffering indicator ──

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        => App.MainWindow?.DispatcherQueue.TryEnqueue(UpdateBuffering);

    /// <summary>
    /// Recomputes <see cref="IsBuffering"/>. Only counts while playback is wanted: a session
    /// restored paused loads its track too, but nobody is waiting on it.
    /// </summary>
    private void UpdateBuffering()
    {
        var state = _player.PlaybackSession.PlaybackState;
        var buffering = _wantsToPlay && (_isFetchingTrack
            || (_isTrackReady && state is MediaPlaybackState.Opening or MediaPlaybackState.Buffering));

        if (buffering == IsBuffering) return;
        IsBuffering = buffering;
        BufferingChanged?.Invoke(buffering);
    }

    private void StartPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = new System.Threading.Timer(_ =>
        {
            var position = Position;
            if (position > TimeSpan.Zero)
                Interlocked.Exchange(ref _lastPositionTicks, position.Ticks);
            if (!_isSeeking)
                PositionChanged?.Invoke(position, Duration);
        }, null, 0, 250);
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
    }


    // TEMP-DIAG: queue/gapless trace in Logs/playtrace.log. Debug builds only; in release the
    // calls, interpolated strings included, are compiled out.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void Trace(string msg) => AppLog.Trace(msg);

    // ── Output device ──

    private DeviceWatcher? _outputWatcher;
    private int _outputVersion;

    /// <summary>Raised on the UI thread when the device playing changes, also when the chosen one goes away or comes back.</summary>
    public event Action? OutputDeviceChanged;

    /// <summary>
    /// The system default followed by every output Windows has enabled now, for the device
    /// pickers, and which of them is in use.
    /// </summary>
    public async Task<(List<AudioOutputDevice> Choices, int Current)> GetOutputChoicesAsync()
    {
        var choices = new List<AudioOutputDevice> { new(string.Empty, Loc.Get("AudioOutput_Default")) };
        try
        {
            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            choices.AddRange(devices.Select(d => new AudioOutputDevice(d.Id, d.Name)).OrderBy(d => d.Name));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlaybackService: listing audio outputs failed: {ex.Message}");
        }

        var activeId = _player.AudioDevice?.Id ?? string.Empty;
        return (choices, Math.Max(0, choices.FindIndex(d => d.Id == activeId)));
    }

    /// <summary>Plays through <paramref name="deviceId"/> from now on and after restarts; empty follows the system default.</summary>
    public Task SetOutputDeviceAsync(string deviceId)
    {
        SettingsService.OutputDeviceId = deviceId.Length > 0 ? deviceId : null;
        return ApplyOutputDeviceAsync();
    }

    // The chosen device may be unplugged or disabled. Play through the default meanwhile and
    // switch back when the watcher sees it again; the setting stays as the user left it.
    private async Task ApplyOutputDeviceAsync()
    {
        var version = ++_outputVersion;
        var id = SettingsService.OutputDeviceId;
        DeviceInformation? device = null;
        if (id is not null)
        {
            try
            {
                // The render selector keeps only enabled endpoints, so an unplugged one isn't found.
                var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
                device = devices.FirstOrDefault(d => d.Id == id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PlaybackService: finding the audio output failed: {ex.Message}");
            }
        }

        // A later choice may have been made while this one was looking.
        if (version != _outputVersion || _player.AudioDevice?.Id == device?.Id) return;
        _player.AudioDevice = device;
        OutputDeviceChanged?.Invoke();
    }

    private void StartOutputWatcher()
    {
        try
        {
            _outputWatcher = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
            _outputWatcher.Added += (_, info) => OnOutputDeviceListChanged(info.Id);
            _outputWatcher.Removed += (_, update) => OnOutputDeviceListChanged(update.Id);
            _outputWatcher.Updated += (_, update) => OnOutputDeviceListChanged(update.Id);
            _outputWatcher.Start();
        }
        catch (Exception ex)
        {
            // Without it the app still plays; it just won't follow the device in and out.
            System.Diagnostics.Debug.WriteLine($"PlaybackService: audio output watcher failed: {ex.Message}");
        }
    }

    // Watcher events arrive on a background thread, and at start once per existing device.
    private void OnOutputDeviceListChanged(string id)
    {
        if (id == SettingsService.OutputDeviceId)
            App.MainWindow?.DispatcherQueue.TryEnqueue(() => _ = ApplyOutputDeviceAsync());
    }

    public void Dispose()
    {
        if (_outputWatcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted })
            _outputWatcher.Stop();
        App.Queue.Queue.CollectionChanged -= OnQueueCollectionChanged;
        App.Queue.ModesChanged -= ScheduleGaplessRevalidation;
        Unsubscribe(_playbackList);
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
        _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
        StopPositionTimer();
        _playTrackCts?.Cancel();
        _playTrackCts?.Dispose();
        _preloadCts?.Cancel();
        _preloadCts?.Dispose();
        _player.Dispose();
    }
}
