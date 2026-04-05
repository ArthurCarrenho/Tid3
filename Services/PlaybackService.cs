using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;
using Windows.Storage.Streams;
using TidalUi3.Helpers;

namespace TidalUi3.Services;

public sealed class PlaybackService : IDisposable
{
    private readonly TidalApiClient _api;
    private readonly MediaPlayer _player;
    private static readonly object _logLock = new();

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
    public event Action? PlaybackEnded;
    public event Action<TimeSpan, TimeSpan>? PositionChanged;
    public event Action? SmtcPlay;
    public event Action? SmtcPause;
    public event Action? SmtcNext;
    public event Action? SmtcPrevious;

    private System.Threading.Timer? _positionTimer;
    private bool _isSeeking;
    private bool _isNativeGaplessTransition;
    private TidalPlaybackInfo? _preloadedPlaybackInfo;
    private TidalManifest? _preloadedManifest;
    private string _preloadedQuality = "";
    private CancellationTokenSource? _preloadCts;
    private CancellationTokenSource? _playTrackCts;

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
        _playbackList.CurrentItemChanged += OnCurrentItemChanged;

        // CommandManager must be enabled for the Windows media overlay notification
        _player.CommandManager.IsEnabled = true;
        _player.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        _player.CommandManager.PlayReceived += (_, args) =>
        {
            _player.Play();
            StartPositionTimer();
            SmtcPlay?.Invoke();
            PlaybackStarted?.Invoke();
        };
        _player.CommandManager.PauseReceived += (_, args) =>
        {
            _player.Pause();
            StopPositionTimer();
            SmtcPause?.Invoke();
            PlaybackPaused?.Invoke();
        };
        _player.CommandManager.NextReceived += (_, _) => SmtcNext?.Invoke();
        _player.CommandManager.PreviousReceived += (_, _) => SmtcPrevious?.Invoke();
        _player.MediaEnded += OnMediaEnded;
        _player.Volume = 0.75;
    }

    public async Task PlayTrackAsync(int trackId, string? coverUrl = null,
        string? title = null, string? artist = null, bool autoPlay = true)
    {
        try
        {
            if (_isNativeGaplessTransition)
            {
                CurrentPlaybackInfo = _preloadedPlaybackInfo;
                CurrentManifest = _preloadedManifest;
                CurrentQuality = _preloadedQuality;
                return;
            }

            // Cancel any previous PlayTrackAsync still awaiting (rapid skipping)
            _playTrackCts?.Cancel();
            _playTrackCts?.Dispose();
            _playTrackCts = new CancellationTokenSource();
            var playCt = _playTrackCts.Token;

            // Cancel any in-flight preload so its stale item cannot land in the list after we clear it
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = new CancellationTokenSource();

            // Immediately clear the list to stop previous track and prevent incorrect preloaded items from playing
            _playbackList.Items.Clear();

            var playbackInfo = await _api.GetPlaybackInfoAsync(trackId, quality: SettingsService.AudioQuality, ct: playCt);
            var mediaMetadata = await _api.GetTrackAsync(trackId, ct: playCt);
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
                    return;
                source = MediaSource.CreateFromUri(new Uri(manifest.Urls[0]));
                CurrentQuality = QualityHelper.FormatQuality(mediaMetadata);
            }

            playCt.ThrowIfCancellationRequested();

            // Use MediaPlaybackItem with display properties for SMTC overlay
            var playbackItem = new MediaPlaybackItem(source);
            var displayProps = playbackItem.GetDisplayProperties();
            displayProps.Type = MediaPlaybackType.Music;
            displayProps.MusicProperties.Title = title ?? "";
            displayProps.MusicProperties.Artist = artist ?? "";
            if (!string.IsNullOrEmpty(coverUrl))
                displayProps.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(coverUrl));
            playbackItem.ApplyDisplayProperties(displayProps);

            _playbackList.Items.Add(playbackItem);

            // Check if we can preload next track for gapless
            if (SettingsService.GaplessPlayback && App.Queue.CurrentIndex + 1 < App.Queue.Queue.Count)
            {
                var nextTrack = App.Queue.Queue[App.Queue.CurrentIndex + 1];
                _ = PreloadNextTrackAsync(nextTrack, _preloadCts.Token);
            }

            if (autoPlay)
            {
                _player.Play();
                StartPositionTimer();
                PlaybackStarted?.Invoke();
            }
            else
            {
                _player.Pause();
                PlaybackPaused?.Invoke();
            }
        }
        catch (OperationCanceledException) { /* superseded by a newer skip — discard silently */ }
        catch (Exception ex)
        {
            CurrentQuality = "";
            LogException("PlaybackError", ex);
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
        _player.Play();
        StartPositionTimer();
        PlaybackStarted?.Invoke();
    }

    public void Pause()
    {
        _player.Pause();
        StopPositionTimer();
        PlaybackPaused?.Invoke();
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
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

    private async Task PreloadNextTrackAsync(Models.Track nextTrack, CancellationToken ct)
    {
        try
        {
            var playbackInfo = await _api.GetPlaybackInfoAsync(nextTrack.Id, quality: SettingsService.AudioQuality, ct: ct);
            var mediaMetadata = await _api.GetTrackAsync(nextTrack.Id, ct: ct);
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

            // Bail out if a newer PlayTrackAsync call already cancelled this preload
            if (ct.IsCancellationRequested) return;

            _preloadedPlaybackInfo = playbackInfo;
            _preloadedManifest = manifest;
            _preloadedQuality = QualityHelper.FormatQuality(mediaMetadata);

            var playbackItem = new MediaPlaybackItem(source);
            var displayProps = playbackItem.GetDisplayProperties();
            displayProps.Type = MediaPlaybackType.Music;
            displayProps.MusicProperties.Title = nextTrack.Title;
            displayProps.MusicProperties.Artist = nextTrack.Artist;
            if (!string.IsNullOrEmpty(nextTrack.CoverUrl))
                displayProps.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(nextTrack.CoverUrl));
            playbackItem.ApplyDisplayProperties(displayProps);

            // Final guard: don't add a stale item if cancelled between the check above and here
            if (!ct.IsCancellationRequested)
                _playbackList.Items.Add(playbackItem);
        }
        catch (OperationCanceledException) { /* cancelled by a subsequent skip — discard silently */ }
        catch (Exception ex)
        {
            LogException("GaplessPreloadError", ex);
        }
    }

    private void OnCurrentItemChanged(MediaPlaybackList sender, CurrentMediaPlaybackItemChangedEventArgs args)
    {
        if (args.NewItem != null && args.Reason == MediaPlaybackItemChangedReason.EndOfStream)
        {
            // All queue and CTS access must happen on the UI thread.
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (App.Queue.CurrentIndex + 1 >= App.Queue.Queue.Count) return;

                _isNativeGaplessTransition = true;
                App.Queue.SetCurrentIndex(App.Queue.CurrentIndex + 1);
                _isNativeGaplessTransition = false;

                // Remove already-played items so the list doesn't grow unboundedly.
                // Keep only the current item onward; the player has already moved past the rest.
                var playedCount = (int)_playbackList.CurrentItemIndex;
                for (var i = 0; i < playedCount; i++)
                    _playbackList.Items.RemoveAt(0);

                PlaybackStarted?.Invoke();

                // Preload the NEXT next track.
                if (SettingsService.GaplessPlayback && App.Queue.CurrentIndex + 1 < App.Queue.Queue.Count)
                {
                    var doubleNextTrack = App.Queue.Queue[App.Queue.CurrentIndex + 1];
                    _preloadCts?.Cancel();
                    _preloadCts?.Dispose();
                    _preloadCts = new CancellationTokenSource();
                    _ = PreloadNextTrackAsync(doubleNextTrack, _preloadCts.Token);
                }
            });
        }
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        // For Gapless playback, MediaEnded might not fire if we move to the next item in the list
        StopPositionTimer();
        PlaybackEnded?.Invoke();
    }

    private void StartPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = new System.Threading.Timer(_ =>
        {
            if (!_isSeeking)
                PositionChanged?.Invoke(Position, Duration);
        }, null, 0, 250);
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
    }

    private static void LogException(string tag, Exception ex)
    {
        try
        {
            lock (_logLock)
            {
                var logDir = System.IO.Path.Combine(
                    Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Logs");
                System.IO.Directory.CreateDirectory(logDir);
                var logPath = System.IO.Path.Combine(logDir, "exceptions.log");
                using var fs = new System.IO.FileStream(logPath,
                    System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                using var sw = new System.IO.StreamWriter(fs);
                sw.WriteLine($"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}");
                sw.WriteLine(ex.ToString());
                sw.WriteLine("\n---");
            }
        }
        catch { }
    }

    public void Dispose()
    {
        StopPositionTimer();
        _playTrackCts?.Cancel();
        _playTrackCts?.Dispose();
        _preloadCts?.Cancel();
        _preloadCts?.Dispose();
        _player.Dispose();
    }
}
