using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Media;
using TS_DJ.Audio.Mixing.Sources;
using TS_DJ.Audio.Playback;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.TeamSpeak;
using TSLib.Helper;

namespace TS_DJ.Audio.Mixing;

public sealed class AudioMixerService : IAudioMixerService
{
    private readonly ILogger<AudioMixerService> _logger;
    private readonly AudioPipeline _pipeline;
    private readonly TeamSpeakService _teamSpeak;
    private readonly IMediaSourceRegistry? _mediaRegistry;
    private readonly IMediaPlaybackLoader _mediaLoader;
    private readonly MediaLoadPrefetchCache? _prefetchCache;
    private readonly MediaLoadTimingTracker? _timingTracker;
    private readonly object _sync = new();
    private readonly MixingSampleProvider _mixer;
    private readonly MixerOutputProducer _outputProducer;
    private readonly CrossfadeController _crossfade;
    private readonly List<PlaybackQueueItem> _queue = [];
    private readonly Stack<PlaybackQueueItem> _playHistory = [];
    private float _masterVolume = AudioValues.HumanVolumeToFactor(50f);
    private int _encoderBitrateKbps = OpusBitratePresets.Default;
    private PlaybackQueueItem? _nowPlaying;
    private bool _isPaused;
    private DeckId _activeDeckId = DeckId.A;
    private PlaybackSettings _playbackSettings = new();
    private int _pendingAdvanceCount;
    private const int StalledReadThreshold = 50;

    public AudioMixerService(
        ILogger<AudioMixerService> logger,
        ILogger<MusicTrackSource> musicLogger,
        ILogger<SoundEffectSource> soundboardLogger,
        ILogger<MixerOutputProducer> outputLogger,
        TeamSpeakService teamSpeak,
        Id pipelineId,
        IMediaPlaybackLoader mediaLoader,
        IMediaSourceRegistry? mediaRegistry = null,
        MediaLoadPrefetchCache? prefetchCache = null,
        MediaLoadTimingTracker? timingTracker = null)
    {
        _mediaRegistry = mediaRegistry;
        _mediaLoader = mediaLoader;
        _prefetchCache = prefetchCache;
        _timingTracker = timingTracker;
        _logger = logger;
        _teamSpeak = teamSpeak;
        _crossfade = new CrossfadeController(_sync)
        {
            Enabled = _playbackSettings.CrossfadeEnabled,
            Duration = TimeSpan.FromSeconds(_playbackSettings.CrossfadeDurationSeconds)
        };

        var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
        _mixer = new MixingSampleProvider(format) { ReadFully = false };

        DeckA = new DeckChannel(DeckId.A, _mixer, _sync, musicLogger);
        DeckB = new DeckChannel(DeckId.B, _mixer, _sync, musicLogger);
        Soundboard = new SoundEffectSource("soundboard", "Soundboard", _mixer, _sync, soundboardLogger, ClipCache);
        Microphone = new MicrophoneSource("microphone", "Microphone");

        _outputProducer = new MixerOutputProducer(
            _mixer,
            _sync,
            HasActiveAudioLocked,
            ProcessLifecycleLocked,
            mixerReadBytes =>
            {
                if (Soundboard is SoundEffectSource soundboard)
                    soundboard.TickCleanup(mixerReadBytes);
            },
            NotifyDecksMixedRead,
            OnMixerReadException,
            outputLogger);

        _pipeline = new AudioPipeline(pipelineId, logger);
        _pipeline.SetTarget(teamSpeak.VoiceTarget);
        _pipeline.SetSource(_outputProducer);
        _pipeline.SetMasterVolume(50f);
        _pipeline.SetEncoderBitrate(_encoderBitrateKbps);

        _logger.LogInformation(
            "AudioMixerService initialized — decks=A+B, soundboard=attached, mic=detached");
    }

    public DeckChannel DeckA { get; }
    IDeckChannel IAudioMixerService.DeckA => DeckA;

    public DeckChannel DeckB { get; }
    IDeckChannel IAudioMixerService.DeckB => DeckB;

    public DeckId ActiveDeckId
    {
        get
        {
            lock (_sync)
                return _activeDeckId;
        }
    }

    public DeckChannel ActiveDeck
    {
        get
        {
            lock (_sync)
                return GetDeckLocked(_activeDeckId);
        }
    }

    IDeckChannel IAudioMixerService.ActiveDeck => ActiveDeck;

    public MusicTrackSource Music => ActiveDeck.Source;
    IMusicTrackSource IAudioMixerService.Music => ActiveDeck;

    public SoundEffectSource Soundboard { get; }
    ISoundEffectSource IAudioMixerService.Soundboard => Soundboard;

    public MicrophoneSource Microphone { get; }
    IMicrophoneSource IAudioMixerService.Microphone => Microphone;

    public PlaybackSettings PlaybackSettings
    {
        get
        {
            lock (_sync)
                return _playbackSettings;
        }
    }

    public bool CrossfadeInProgress
    {
        get
        {
            lock (_sync)
                return _crossfade.IsActive;
        }
    }

    public bool AdvancePending
    {
        get
        {
            lock (_sync)
                return _pendingAdvanceCount > 0;
        }
    }

    public PreloadedClipCache ClipCache { get; } = new();

    public IReadOnlyList<PlaybackQueueItem> Queue
    {
        get
        {
            lock (_sync)
                return _queue.ToList();
        }
    }

    public PlaybackQueueItem? NowPlaying
    {
        get
        {
            lock (_sync)
                return _nowPlaying;
        }
    }

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0f, 1f);
            _pipeline.SetMasterVolume(AudioValues.FactorToHumanVolume(_masterVolume));
        }
    }

    public int EncoderBitrateKbps
    {
        get => _encoderBitrateKbps;
        set
        {
            var normalized = OpusBitratePresets.Normalize(value);
            if (_encoderBitrateKbps == normalized)
                return;

            _encoderBitrateKbps = normalized;
            _pipeline.SetEncoderBitrate(_encoderBitrateKbps);
            EncoderBitrateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public float SoundboardVolume
    {
        get => Soundboard.Volume;
        set => Soundboard.Volume = Math.Clamp(value, 0f, 1f);
    }

    public event EventHandler? QueueChanged;
    public event EventHandler? NowPlayingChanged;
    public event EventHandler? AdvancePendingChanged;
    public event EventHandler<DeckStateChangedEventArgs>? DeckStateChanged;
    public event EventHandler<PlaybackModeChangedEventArgs>? PlaybackModeChanged;
    public event EventHandler? EncoderBitrateChanged;

    public void SetPlaybackSettings(PlaybackSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            _playbackSettings = new PlaybackSettings
            {
                Mode = settings.Mode,
                CrossfadeEnabled = settings.CrossfadeEnabled,
                CrossfadeDurationSeconds = settings.CrossfadeDurationSeconds
            };
            _crossfade.Enabled = settings.CrossfadeEnabled;
            _crossfade.Duration = TimeSpan.FromSeconds(settings.CrossfadeDurationSeconds);
        }

        PlaybackModeChanged?.Invoke(this, new PlaybackModeChangedEventArgs(settings.Mode));
    }

    public void Enqueue(string filePath) =>
        Enqueue(PlaybackQueueItem.FromLocalFile(filePath));

    public void Enqueue(PlaybackQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateQueueItem(item);

        lock (_sync)
        {
            var clone = CloneQueueItem(item);
            clone.Status = PlaybackQueueStatus.Queued;
            _queue.Add(clone);
        }

        if (MediaLoadPrefetchCache.ShouldPrefetch(item))
            _prefetchCache?.StartPrefetch(item);
        RaiseQueueChanged();
    }

    public void EnqueueRange(IEnumerable<PlaybackQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        PlaybackQueueItem? firstNonLocal = null;

        lock (_sync)
        {
            foreach (var item in items)
            {
                ValidateQueueItem(item);
                var clone = CloneQueueItem(item);
                clone.Status = PlaybackQueueStatus.Queued;
                _queue.Add(clone);

                if (firstNonLocal is null && MediaLoadPrefetchCache.ShouldPrefetch(clone))
                    firstNonLocal = clone;
            }
        }

        if (firstNonLocal is not null)
            _prefetchCache?.StartPrefetch(firstNonLocal);

        RaiseQueueChanged();
    }

    public void UpdateQueueItemMetadata(
        string sourceKey,
        string? displayName = null,
        string? artist = null,
        int? durationSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return;

        lock (_sync)
        {
            var item = _queue.FirstOrDefault(i => i.SourceKey == sourceKey);
            if (item is null)
                return;

            if (!string.IsNullOrWhiteSpace(displayName))
                item.DisplayName = displayName;

            if (artist is not null)
                item.Artist = string.IsNullOrWhiteSpace(artist) ? null : artist;

            if (durationSeconds is > 0)
                item.DurationSeconds = durationSeconds;
        }

        RaiseQueueChanged();
    }

    public void RemoveFromQueue(int index)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _queue.Count)
                return;

            var item = _queue[index];
            if (item.Status == PlaybackQueueStatus.Playing)
                return;

            _queue.RemoveAt(index);
        }

        RaiseQueueChanged();
    }

    public void ClearQueue()
    {
        lock (_sync)
            _queue.RemoveAll(item => item.Status != PlaybackQueueStatus.Playing);

        RaiseQueueChanged();
    }

    public bool CanSkipPrevious
    {
        get
        {
            lock (_sync)
                return _playbackSettings.Mode == PlaybackMode.SequentialQueue
                    && (_nowPlaying is not null || _playHistory.Count > 0);
        }
    }

    public void PlayQueueItem(int index)
    {
        if (_playbackSettings.Mode != PlaybackMode.SequentialQueue)
            return;

        PlaybackQueueItem? target;
        lock (_sync)
        {
            if (!TryGetQueueItemLocked(index, out target))
                return;
        }

        ScheduleAsyncAdvanceToItem(target!);
    }

    public Task PlayQueueItemAsync(int index, CancellationToken cancellationToken = default)
    {
        if (_playbackSettings.Mode != PlaybackMode.SequentialQueue)
            return Task.CompletedTask;

        PlaybackQueueItem? target;
        lock (_sync)
        {
            if (!TryGetQueueItemLocked(index, out target))
                return Task.CompletedTask;
        }

        return AdvanceToQueueItemAsync(target!, cancellationToken);
    }

    public async Task LoadToDeckAsync(
        DeckId deckId,
        PlaybackQueueItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateQueueItem(item);

        var loadResult = await LoadMediaForItemAsync(item, cancellationToken);

        lock (_sync)
        {
            var deck = GetDeckLocked(deckId);
            deck.Open(item, loadResult);
            RaiseDeckStateChangedLocked(deck);
        }
    }

    public async Task PlayDeckAsync(DeckId deckId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            var target = GetDeckLocked(deckId);
            if (target.LoadedItem is null)
                throw new InvalidOperationException($"Deck {deckId} has no loaded track.");

            var other = GetInactiveDeckLocked(deckId);

            if (_playbackSettings.Mode == PlaybackMode.DualDeck
                && other.IsOutputting)
            {
                _activeDeckId = deckId;
                _crossfade.StartCrossfade(other, target, () =>
                {
                    lock (_sync)
                    {
                        _activeDeckId = deckId;
                        RaiseDeckStateChangedLocked(other);
                        RaiseDeckStateChangedLocked(target);
                    }
                });

                if (!_pipeline.IsTransmitting)
                    BeginTransmissionLocked();
                else if (!_crossfade.IsActive)
                    _pipeline.ResyncForNewTrack();

                RaiseDeckStateChangedLocked(target);
                return;
            }

            if (other.IsOutputting)
            {
                other.Cue();
                other.ResetCrossfadeGain();
                RaiseDeckStateChangedLocked(other);
            }

            _activeDeckId = deckId;
            target.ResetCrossfadeGain();
            target.Play();

            if (!_pipeline.IsTransmitting)
                BeginTransmissionLocked();
            else
                _pipeline.ResyncForNewTrack();

            RaiseDeckStateChangedLocked(target);
        }

        await Task.CompletedTask;
    }

    public void StopDeck(DeckId deckId)
    {
        lock (_sync)
        {
            var deck = GetDeckLocked(deckId);
            deck.Stop();
            RaiseDeckStateChangedLocked(deck);

            if (!HasActiveAudioLocked())
                StopTransmissionLocked("deck stopped");
        }
    }

    public void CueDeck(DeckId deckId)
    {
        lock (_sync)
        {
            var deck = GetDeckLocked(deckId);
            deck.Cue();
            RaiseDeckStateChangedLocked(deck);

            if (!HasActiveAudioLocked())
                StopTransmissionLocked("deck cued");
        }
    }

    public void SkipNext()
    {
        if (_playbackSettings.Mode != PlaybackMode.SequentialQueue)
            return;

        PlaybackQueueItem? next;
        PlaybackQueueItem? currentItem;
        lock (_sync)
        {
            PushCurrentToHistoryLocked();

            currentItem = _nowPlaying;
            if (currentItem is not null)
                currentItem.Status = PlaybackQueueStatus.Played;

            next = _queue.FirstOrDefault(item => item.Status == PlaybackQueueStatus.Queued);
            if (next is null)
            {
                if (!AnyDeckOutputtingLocked() && !_crossfade.IsActive)
                {
                    _nowPlaying = null;
                    EnterIdleLocked("skip next — queue empty");
                }
                else if (currentItem is not null)
                    _nowPlaying = currentItem;
            }
        }

        if (next is not null)
            ScheduleAsyncAdvanceToItem(next);

        RaiseNowPlayingChanged();
        RaiseQueueChanged();
    }

    public void SkipPrevious()
    {
        if (_playbackSettings.Mode != PlaybackMode.SequentialQueue)
            return;

        var changed = false;
        lock (_sync)
        {
            if (_nowPlaying is null && _playHistory.Count == 0)
                return;

            var active = ActiveDeck;
            if (_nowPlaying is not null && active.CurrentTime > TimeSpan.FromSeconds(3))
            {
                active.Play();
                _pipeline.ResyncForNewTrack();
                if (!_pipeline.IsTransmitting)
                    BeginTransmissionLocked();
                changed = true;
            }
            else if (_playHistory.Count > 0)
            {
                var previousItem = _playHistory.Pop();
                var index = EnsureQueuedItemLocked(previousItem);
                PushCurrentToHistoryLocked();
                DemoteCurrentPlayingLocked();
                var target = _queue[index];
                ScheduleAsyncAdvanceToItem(target);
                changed = true;
            }
        }

        if (changed)
        {
            RaiseNowPlayingChanged();
            RaiseQueueChanged();
        }
    }

    public void Start()
    {
        PlaybackQueueItem? next = null;
        lock (_sync)
        {
            if (_playbackSettings.Mode == PlaybackMode.DualDeck)
            {
                var deck = ActiveDeck;
                if (_isPaused && deck.HasActiveTrack)
                {
                    deck.ResumeOutput();
                    BeginTransmissionLocked();
                    _isPaused = false;
                    return;
                }

                if (deck.IsOutputting && !_pipeline.IsTransmitting)
                    BeginTransmissionLocked();
                return;
            }

            if (_isPaused && ActiveDeck.HasActiveTrack)
            {
                ActiveDeck.ResumeOutput();
                BeginTransmissionLocked();
                _isPaused = false;
                return;
            }

            if (ActiveDeck.IsOutputting)
            {
                if (!_pipeline.IsTransmitting)
                    BeginTransmissionLocked();
                return;
            }

            next = _queue.FirstOrDefault(item => item.Status == PlaybackQueueStatus.Queued);
            if (next is null)
                return;
        }

        ScheduleAsyncAdvanceToItem(next);
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (!ActiveDeck.HasActiveTrack && !DeckA.HasActiveTrack && !DeckB.HasActiveTrack && !_isPaused)
                return;

            ActiveDeck.PauseOutput();
            _isPaused = ActiveDeck.HasActiveTrack;

            if (!HasActiveAudioLocked())
                StopTransmissionLocked("paused");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _crossfade.Cancel(DeckA, DeckB);

            if (_nowPlaying is not null)
            {
                if (ActiveDeck.IsStalled(StalledReadThreshold) || !ActiveDeck.IsPlaying)
                    _nowPlaying.Status = PlaybackQueueStatus.Played;
                else
                    _nowPlaying.Status = PlaybackQueueStatus.Queued;
                _nowPlaying = null;
            }

            DeckA.Cue();
            DeckB.Cue();
            _isPaused = false;
            _playHistory.Clear();

            if (Soundboard.IsActive)
            {
                if (!_pipeline.IsTransmitting)
                    BeginTransmissionLocked(forSoundboardOnly: true);
            }
            else
                StopTransmissionLocked("stopped");
        }

        RaiseDeckStateChangedLocked(DeckA);
        RaiseDeckStateChangedLocked(DeckB);
        RaiseNowPlayingChanged();
        RaiseQueueChanged();
    }

    public void PlaySoundEffect(string filePath, float gainFactor = 1f)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        if (!Decoding.AudioFileDecoder.IsSupportedFile(filePath))
        {
            throw new NotSupportedException(
                $"Unsupported audio format '{Path.GetExtension(filePath)}'. Supported: MP3, WAV, AIFF, FLAC.");
        }

        lock (_sync)
        {
            Soundboard.Play(filePath, gainFactor);

            if (HasActiveAudioLocked() && !_pipeline.IsTransmitting)
                BeginTransmissionLocked(forSoundboardOnly: _isPaused || !AnyDeckOutputtingLocked());
        }
    }

    private bool StartQueueItemOnActiveDeckLocked(PlaybackQueueItem target, MediaLoadResult loadResult)
    {
        ArgumentNullException.ThrowIfNull(loadResult);

        if (target.Status == PlaybackQueueStatus.Playing)
            return false;

        PushCurrentToHistoryLocked();
        DemoteCurrentPlayingLocked();

        target.Status = PlaybackQueueStatus.Playing;
        _nowPlaying = target;

        try
        {
            var load = loadResult;
            var outgoing = ActiveDeck;
            var incoming = outgoing.IsOutputting ? GetInactiveDeckLocked() : outgoing;

            if (outgoing.IsOutputting
                && incoming.DeckId != outgoing.DeckId
                && _crossfade.Enabled
                && _playbackSettings.CrossfadeEnabled)
            {
                incoming.Open(target, load);
                PrefetchNextQueuedLocked();
                _crossfade.StartCrossfade(outgoing, incoming, () =>
                {
                    lock (_sync)
                    {
                        _activeDeckId = incoming.DeckId;
                        RaiseDeckStateChangedLocked(outgoing);
                        RaiseDeckStateChangedLocked(incoming);
                    }
                });
                _activeDeckId = incoming.DeckId;
            }
            else
            {
                if (outgoing.IsOutputting && incoming.DeckId != outgoing.DeckId)
                {
                    outgoing.Cue();
                    outgoing.ResetCrossfadeGain();
                    RaiseDeckStateChangedLocked(outgoing);
                }

                incoming.Open(target, load);
                incoming.ResetCrossfadeGain();
                incoming.Play();
                _activeDeckId = incoming.DeckId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play queue item: {SourceKey}", target.SourceKey);
            target.Status = PlaybackQueueStatus.Failed;
            _nowPlaying = null;
            RaiseQueueChanged();
            return false;
        }

        if (!_pipeline.IsTransmitting)
            BeginTransmissionLocked();
        else if (!_crossfade.IsActive)
            _pipeline.ResyncForNewTrack();

        RaiseDeckStateChangedLocked(ActiveDeck);
        return true;
    }

    private void HandleTrackFinishedLocked(DeckChannel deck)
    {
        if (_playbackSettings.Mode == PlaybackMode.DualDeck)
        {
            deck.Cue();
            RaiseDeckStateChangedLocked(deck);
            deck.NotifyTrackEndedEvent();

            if (!HasActiveAudioLocked())
                EnterIdleLocked("deck finished");

            return;
        }

        if (_crossfade.IsActive && deck.DeckId != ActiveDeckId)
            return;

        if (deck.DeckId != ActiveDeckId)
            return;

        var finished = _nowPlaying;
        if (finished is not null)
        {
            finished.Status = PlaybackQueueStatus.Played;
            if (_playHistory.Count == 0 || _playHistory.Peek().SourceKey != finished.SourceKey)
                _playHistory.Push(CloneQueueItem(finished));
        }

        if (_queue.Any(i => i.Status == PlaybackQueueStatus.Queued))
        {
            var next = _queue.FirstOrDefault(item => item.Status == PlaybackQueueStatus.Queued);
            if (next is not null)
                ScheduleAsyncAdvanceToItem(next);
            else if (!HasActiveAudioLocked())
                EnterIdleLocked("queue advance failed");
        }
        else
        {
            if (!HasActiveAudioLocked())
            {
                _nowPlaying = null;
                EnterIdleLocked("queue finished");
            }
        }

        RaiseNowPlayingChanged();
        RaiseQueueChanged();
        deck.NotifyTrackEndedEvent();
    }

    private void HandleTrackDecodeFailedLocked(DeckChannel deck, Exception? error)
    {
        if (_playbackSettings.Mode == PlaybackMode.DualDeck)
        {
            deck.Cue();
            RaiseDeckStateChangedLocked(deck);
            if (!HasActiveAudioLocked())
                EnterIdleLocked("deck decode failure");
            return;
        }

        if (deck.DeckId != ActiveDeckId)
            return;

        if (_nowPlaying is not null)
            _nowPlaying.Status = PlaybackQueueStatus.Failed;

        if (_queue.Any(i => i.Status == PlaybackQueueStatus.Queued))
        {
            var next = _queue.FirstOrDefault(item => item.Status == PlaybackQueueStatus.Queued);
            if (next is not null)
                ScheduleAsyncAdvanceToItem(next);
            else if (!HasActiveAudioLocked())
                EnterIdleLocked("decode failure — queue recovery exhausted");
        }
        else if (!HasActiveAudioLocked())
            EnterIdleLocked("decode failure — queue empty");

        RaiseNowPlayingChanged();
        RaiseQueueChanged();
    }

    private void ProcessLifecycleLocked()
    {
        _crossfade.Tick();
        MaybePrefetchLookaheadLocked();

        foreach (var deck in new[] { DeckA, DeckB })
        {
            if (deck.TryConsumeTrackDecodeFailure(out var failedPath, out var decodeError))
            {
                _logger.LogWarning(decodeError, "Decode failure on {Deck}: {SourceKey}", deck.DeckId, failedPath);
                HandleTrackDecodeFailedLocked(deck, decodeError);
                return;
            }

            if (deck.TryConsumeTrackEnd(out var finishedPath))
            {
                _logger.LogInformation("EOF on {Deck}: {SourceKey}", deck.DeckId, finishedPath);
                HandleTrackFinishedLocked(deck);
                return;
            }

            if (deck.IsStalled(StalledReadThreshold))
            {
                _logger.LogWarning("Stalled reads on {Deck}: {SourceKey}", deck.DeckId, deck.CurrentFilePath);
                deck.ForceTrackEnd();
                return;
            }
        }

        if (!HasActiveAudioLocked() && _pipeline.IsTransmitting)
            EnterIdleLocked("no active sources during read");
    }

    private void MaybePrefetchLookaheadLocked()
    {
        if (_prefetchCache is null || _timingTracker is null)
            return;

        if (_playbackSettings.Mode != PlaybackMode.SequentialQueue)
            return;

        var next = _queue.FirstOrDefault(i => i.Status == PlaybackQueueStatus.Queued);
        if (next is null || _prefetchCache.IsReady(next) || _prefetchCache.IsInProgress(next))
            return;

        var deck = ActiveDeck;
        if (!deck.IsOutputting)
            return;

        var remaining = deck.TotalTime - deck.CurrentTime;
        if (remaining <= TimeSpan.Zero)
            return;

        var estimatedLoad = TimeSpan.FromMilliseconds(_timingTracker.EstimatedLoadMs(next.SourceKind));
        var crossfade = TimeSpan.FromSeconds(_playbackSettings.CrossfadeDurationSeconds);
        if (remaining > crossfade + estimatedLoad)
            return;

        _prefetchCache.StartPrefetch(next);
    }

    private void NotifyDecksMixedRead(int sampleBytes)
    {
        DeckA.NotifyMixedRead(sampleBytes);
        DeckB.NotifyMixedRead(sampleBytes);
    }

    private void OnMixerReadException(Exception ex)
    {
        if (DeckA.IsOutputting)
            DeckA.AbortTrackDueToDecodeError(ex);
        if (DeckB.IsOutputting)
            DeckB.AbortTrackDueToDecodeError(ex);
    }

    private bool HasActiveAudioLocked() =>
        AnyDeckOutputtingLocked() || Soundboard.IsActive || _crossfade.IsActive;

    private bool AnyDeckOutputtingLocked() =>
        DeckA.IsOutputting || DeckB.IsOutputting;

    private DeckChannel GetDeckLocked(DeckId deckId) =>
        deckId == DeckId.A ? DeckA : DeckB;

    private DeckChannel GetInactiveDeckLocked() =>
        _activeDeckId == DeckId.A ? DeckB : DeckA;

    private DeckChannel GetInactiveDeckLocked(DeckId deckId) =>
        deckId == DeckId.A ? DeckB : DeckA;

    private bool TryGetQueueItemLocked(int index, out PlaybackQueueItem item)
    {
        item = null!;
        if (index < 0 || index >= _queue.Count)
            return false;

        item = _queue[index];
        return item.Status != PlaybackQueueStatus.Playing;
    }

    private void ValidateQueueItem(PlaybackQueueItem item)
    {
        var source = _mediaRegistry?.FindForItem(item);
        if (source is not null)
        {
            source.ValidateForEnqueue(item);
            return;
        }

        if (item.SourceKind == PlaybackSourceKind.LocalFile)
        {
            if (!File.Exists(item.FilePath))
                throw new FileNotFoundException("Audio file not found.", item.FilePath);

            if (!Decoding.AudioFileDecoder.IsSupportedFile(item.FilePath))
            {
                throw new NotSupportedException(
                    $"Unsupported audio format '{Path.GetExtension(item.FilePath)}'. Supported: MP3, WAV, AIFF, FLAC.");
            }

            return;
        }

        if (item.SourceKind == PlaybackSourceKind.YouTube)
        {
            if (string.IsNullOrWhiteSpace(item.VideoUrl))
                throw new ArgumentException("YouTube queue items require a video URL.", nameof(item));
            return;
        }

        if (string.IsNullOrWhiteSpace(item.RemoteTrackId))
            throw new ArgumentException("Remote queue items require a track id.", nameof(item));
    }

    private void PushCurrentToHistoryLocked()
    {
        if (_nowPlaying is null)
            return;

        if (_playHistory.Count == 0 || _playHistory.Peek().SourceKey != _nowPlaying.SourceKey)
            _playHistory.Push(CloneQueueItem(_nowPlaying));
    }

    private void DemoteCurrentPlayingLocked()
    {
        foreach (var item in _queue)
        {
            if (item.Status == PlaybackQueueStatus.Playing)
                item.Status = PlaybackQueueStatus.Queued;
        }
    }

    private int EnsureQueuedItemLocked(PlaybackQueueItem item)
    {
        for (var i = 0; i < _queue.Count; i++)
        {
            if (_queue[i].SourceKey == item.SourceKey)
            {
                if (_queue[i].Status != PlaybackQueueStatus.Playing)
                    _queue[i].Status = PlaybackQueueStatus.Queued;
                return i;
            }
        }

        var clone = CloneQueueItem(item);
        clone.Status = PlaybackQueueStatus.Queued;
        _queue.Insert(0, clone);
        return 0;
    }

    private static PlaybackQueueItem CloneQueueItem(PlaybackQueueItem item) =>
        new()
        {
            SourceKind = item.SourceKind,
            FilePath = item.FilePath,
            RemoteTrackId = item.RemoteTrackId,
            VideoUrl = item.VideoUrl,
            ThumbnailUrl = item.ThumbnailUrl,
            DisplayName = item.DisplayName,
            Artist = item.Artist,
            Album = item.Album,
            DurationSeconds = item.DurationSeconds,
            Status = item.Status
        };

    private void PrefetchNextQueuedLocked()
    {
        if (_prefetchCache is null)
            return;

        bool crossfadeEnabled;
        IReadOnlyList<PlaybackQueueItem> queueSnapshot;
        lock (_sync)
        {
            crossfadeEnabled = _playbackSettings.CrossfadeEnabled;
            queueSnapshot = _queue.ToList();
        }

        _prefetchCache.PrefetchNextQueued(queueSnapshot, crossfadeEnabled);
    }

    private void SetAdvancePending(bool pending)
    {
        var changed = false;
        lock (_sync)
        {
            if (pending)
            {
                _pendingAdvanceCount++;
                changed = _pendingAdvanceCount == 1;
            }
            else if (_pendingAdvanceCount > 0)
            {
                _pendingAdvanceCount--;
                changed = _pendingAdvanceCount == 0;
            }
        }

        if (changed)
            AdvancePendingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleAsyncAdvanceToItem(PlaybackQueueItem target)
    {
        SetAdvancePending(true);
        _ = Task.Run(async () =>
        {
            try
            {
                await AdvanceToQueueItemAsync(target, CancellationToken.None);
            }
            finally
            {
                SetAdvancePending(false);
            }
        });
    }

    private async Task AdvanceToQueueItemAsync(PlaybackQueueItem target, CancellationToken cancellationToken)
    {
        MediaLoadResult loadResult;
        try
        {
            loadResult = await LoadMediaForItemAsync(target, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load media for {SourceKey}", target.SourceKey);
            MarkQueueItemFailed(target.SourceKey);
            return;
        }

        bool started;
        lock (_sync)
        {
            var queueItem = _queue.FirstOrDefault(i => i.SourceKey == target.SourceKey);
            if (queueItem is null || queueItem.Status == PlaybackQueueStatus.Failed)
            {
                loadResult.StreamHandle?.Dispose();
                return;
            }

            started = StartQueueItemOnActiveDeckLocked(queueItem, loadResult);
        }

        if (started)
        {
            PrefetchNextQueuedLocked();
            RaiseNowPlayingChanged();
            RaiseQueueChanged();
        }
        else
            loadResult.StreamHandle?.Dispose();
    }

    private async Task<MediaLoadResult> LoadMediaForItemAsync(
        PlaybackQueueItem item,
        CancellationToken cancellationToken)
    {
        if (_prefetchCache?.TryTakeReady(item, out var prefetched) == true && prefetched is not null)
            return prefetched;

        if (_prefetchCache is not null && _prefetchCache.IsInProgress(item))
            return await _prefetchCache.WaitForReadyAsync(item, cancellationToken);

        return await _mediaLoader.LoadAsync(item, cancellationToken: cancellationToken);
    }

    private void MarkQueueItemFailed(string sourceKey)
    {
        lock (_sync)
        {
            var item = _queue.FirstOrDefault(i => i.SourceKey == sourceKey);
            if (item is not null)
                item.Status = PlaybackQueueStatus.Failed;
        }

        RaiseQueueChanged();
    }

    private void MarkQueueItemFailedLocked(int index)
    {
        lock (_sync)
        {
            if (index >= 0 && index < _queue.Count)
                _queue[index].Status = PlaybackQueueStatus.Failed;
        }

        RaiseQueueChanged();
    }

    private void BeginTransmissionLocked(bool forSoundboardOnly = false)
    {
        _pipeline.ResetTimer();
        _pipeline.Start();
        if (!forSoundboardOnly)
            _isPaused = false;
    }

    private void StopTransmissionLocked(string reason)
    {
        _pipeline.Pause();
        _teamSpeak.VoiceTarget.LogTransmissionStopped(reason);
    }

    private void EnterIdleLocked(string reason)
    {
        StopTransmissionLocked(reason);
        _isPaused = false;
    }

    private void RaiseQueueChanged() => QueueChanged?.Invoke(this, EventArgs.Empty);
    private void RaiseNowPlayingChanged() => NowPlayingChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseDeckStateChangedLocked(DeckChannel deck) =>
        DeckStateChanged?.Invoke(this, new DeckStateChangedEventArgs(deck.DeckId, deck.LoadedItem, deck.IsPlaying));

    public void Dispose()
    {
        Stop();
        DeckA.Dispose();
        DeckB.Dispose();
        Soundboard.Dispose();
        Microphone.Dispose();
        _pipeline.Dispose();
        _outputProducer.Dispose();
    }
}
