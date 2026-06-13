using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Mixing.Sources;
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
    private readonly IPlaybackStreamUrlProvider? _streamUrlProvider;
    private readonly object _sync = new();
    private readonly MixingSampleProvider _mixer;
    private readonly MixerOutputProducer _outputProducer;
    private readonly List<PlaybackQueueItem> _queue = [];
    private readonly Stack<PlaybackQueueItem> _playHistory = [];
    private float _masterVolume = AudioValues.HumanVolumeToFactor(50f);
    private int _encoderBitrateKbps = OpusBitratePresets.Default;
    private PlaybackQueueItem? _nowPlaying;
    private bool _isPaused;
    private const int StalledReadThreshold = 50;

    public PreloadedClipCache ClipCache { get; } = new();

    public AudioMixerService(
        ILogger<AudioMixerService> logger,
        ILogger<MusicTrackSource> musicLogger,
        ILogger<SoundEffectSource> soundboardLogger,
        ILogger<MixerOutputProducer> outputLogger,
        TeamSpeakService teamSpeak,
        Id pipelineId,
        IPlaybackStreamUrlProvider? streamUrlProvider = null)
    {
        _streamUrlProvider = streamUrlProvider;
        _logger = logger;
        _teamSpeak = teamSpeak;

        var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
        _mixer = new MixingSampleProvider(format)
        {
            ReadFully = false
        };

        Music = new MusicTrackSource("music", "Music", _mixer, _sync, musicLogger);
        Soundboard = new SoundEffectSource("soundboard", "Soundboard", _mixer, _sync, soundboardLogger, ClipCache);
        Microphone = new MicrophoneSource("microphone", "Microphone");

        _outputProducer = new MixerOutputProducer(
            _mixer,
            _sync,
            HasActiveAudioLocked,
            ProcessLifecycleLocked,
            () =>
            {
                if (Soundboard is SoundEffectSource soundboard)
                    soundboard.TickCleanup();
            },
            Music.NotifyMixedRead,
            outputLogger);

        _pipeline = new AudioPipeline(pipelineId, logger);
        _pipeline.SetTarget(teamSpeak.VoiceTarget);
        _pipeline.SetSource(_outputProducer);
        _pipeline.SetMasterVolume(50f);
        _pipeline.SetEncoderBitrate(_encoderBitrateKbps);

        _logger.LogInformation(
            "AudioMixerService initialized — mixer inputs: music=attached, soundboard=attached, mic=detached, ReadFully=false");
    }

    public MusicTrackSource Music { get; }
    IMusicTrackSource IAudioMixerService.Music => Music;

    public SoundEffectSource Soundboard { get; }
    ISoundEffectSource IAudioMixerService.Soundboard => Soundboard;

    public MicrophoneSource Microphone { get; }
    IMicrophoneSource IAudioMixerService.Microphone => Microphone;

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
            _logger.LogInformation("Opus encoder bitrate changed to {BitrateKbps} kbps ({BitrateBps} bps)",
                _encoderBitrateKbps, _encoderBitrateKbps * 1000);
            EncoderBitrateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public float SoundboardVolume
    {
        get => Soundboard.Volume;
        set => Soundboard.Volume = Math.Clamp(value, 0f, 1f);
    }

    public void PlaySoundEffect(string filePath)
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
            Soundboard.Play(filePath);

            var active = HasActiveAudioLocked();
            var transmitting = _pipeline.IsTransmitting;
            var sfxCount = Soundboard.ActiveEffectCount;

            _logger.LogInformation(
                "PlaySoundEffect state: active={Active}, transmitting={Transmitting}, sfxLive={SfxCount}, channelAttached={Attached}, musicOutputting={MusicOutputting}",
                active, transmitting, sfxCount, Soundboard.IsChannelAttached, Music.IsOutputting);

            if (active && !transmitting)
                BeginTransmissionLocked(forSoundboardOnly: _isPaused || !Music.IsOutputting);
        }

        _logger.LogInformation("Sound effect triggered: {FilePath}", filePath);
    }

    public event EventHandler? QueueChanged;
    public event EventHandler? NowPlayingChanged;
    public event EventHandler? EncoderBitrateChanged;

    public void Enqueue(string filePath) =>
        Enqueue(PlaybackQueueItem.FromLocalFile(filePath));

    public void Enqueue(PlaybackQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateQueueItem(item);

        lock (_sync)
        {
            _queue.Add(new PlaybackQueueItem
            {
                SourceKind = item.SourceKind,
                FilePath = item.FilePath,
                RemoteTrackId = item.RemoteTrackId,
                DisplayName = item.DisplayName,
                Artist = item.Artist,
                Album = item.Album,
                DurationSeconds = item.DurationSeconds,
                Status = PlaybackQueueStatus.Queued
            });

            _logger.LogInformation(
                "Queue enqueue: {SourceKey} (queue size={QueueSize}, queued={QueuedCount})",
                item.SourceKey, _queue.Count, _queue.Count(i => i.Status == PlaybackQueueStatus.Queued));
        }

        RaiseQueueChanged();
    }

    public void EnqueueRange(IEnumerable<PlaybackQueueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        lock (_sync)
        {
            var added = 0;
            foreach (var item in items)
            {
                ValidateQueueItem(item);
                _queue.Add(new PlaybackQueueItem
                {
                    SourceKind = item.SourceKind,
                    FilePath = item.FilePath,
                    RemoteTrackId = item.RemoteTrackId,
                    DisplayName = item.DisplayName,
                    Artist = item.Artist,
                    Album = item.Album,
                    DurationSeconds = item.DurationSeconds,
                    Status = PlaybackQueueStatus.Queued
                });
                added++;
            }

            _logger.LogInformation(
                "Queue enqueue range: {Added} item(s) (queue size={QueueSize}, queued={QueuedCount})",
                added, _queue.Count, _queue.Count(i => i.Status == PlaybackQueueStatus.Queued));
        }

        RaiseQueueChanged();
    }

    private void ValidateQueueItem(PlaybackQueueItem item)
    {
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

        if (string.IsNullOrWhiteSpace(item.RemoteTrackId))
            throw new ArgumentException("Remote queue items require a track id.", nameof(item));

        if (_streamUrlProvider is null)
            throw new InvalidOperationException("Remote playback is not configured.");
    }

    private void OpenQueueItemLocked(PlaybackQueueItem item)
    {
        if (item.SourceKind == PlaybackSourceKind.LocalFile)
        {
            Music.OpenPlaybackItem(item);
            return;
        }

        var streamUrl = _streamUrlProvider?.TryGetStreamUrl(item);
        if (string.IsNullOrWhiteSpace(streamUrl))
            throw new InvalidOperationException("Could not resolve remote stream URL.");

        Music.OpenPlaybackItem(item, streamUrl);
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
            _logger.LogInformation("Queue remove index {Index}: {SourceKey}", index, item.SourceKey);
        }

        RaiseQueueChanged();
    }

    public void ClearQueue()
    {
        lock (_sync)
        {
            var removed = _queue.RemoveAll(item => item.Status != PlaybackQueueStatus.Playing);
            _logger.LogInformation("Queue cleared ({Removed} items removed)", removed);
        }

        RaiseQueueChanged();
    }

    public bool CanSkipPrevious
    {
        get
        {
            lock (_sync)
                return _nowPlaying is not null || _playHistory.Count > 0;
        }
    }

    public void PlayQueueItem(int index)
    {
        bool started;
        lock (_sync)
        {
            if (index < 0 || index >= _queue.Count)
                return;

            var target = _queue[index];
            if (target.Status == PlaybackQueueStatus.Playing)
                return;

            PushCurrentToHistoryLocked();
            DemoteCurrentPlayingLocked();

            target.Status = PlaybackQueueStatus.Playing;
            _nowPlaying = target;

            try
            {
                OpenQueueItemLocked(target);
                Music.Play();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play queue item at index {Index}: {SourceKey}", index, target.SourceKey);
                target.Status = PlaybackQueueStatus.Failed;
                _nowPlaying = null;
                RaiseQueueChanged();
                return;
            }

            if (!_pipeline.IsTransmitting)
                BeginTransmissionLocked();
            else
                _pipeline.ResyncForNewTrack();

            started = true;
            _logger.LogInformation("PlayQueueItem index {Index}: {SourceKey}", index, target.SourceKey);
        }

        if (started)
        {
            RaiseNowPlayingChanged();
            RaiseQueueChanged();
        }
    }

    public void SkipNext()
    {
        bool advanced;
        lock (_sync)
        {
            PushCurrentToHistoryLocked();

            if (_nowPlaying is not null)
            {
                _nowPlaying.Status = PlaybackQueueStatus.Played;
                _nowPlaying = null;
            }

            if (!TryStartNextQueuedTrackLocked())
            {
                if (Soundboard.IsActive)
                {
                    _nowPlaying = null;
                    if (!_pipeline.IsTransmitting)
                        BeginTransmissionLocked();
                }
                else
                    EnterIdleLocked("skip next — queue empty");
                advanced = false;
            }
            else
            {
                _pipeline.ResyncForNewTrack();
                if (!_pipeline.IsTransmitting)
                    BeginTransmissionLocked();
                advanced = true;
            }

            _logger.LogInformation("SkipNext — advanced={Advanced}", advanced);
        }

        RaiseNowPlayingChanged();
        RaiseQueueChanged();
    }

    public void SkipPrevious()
    {
        var changed = false;
        lock (_sync)
        {
            if (_nowPlaying is null && _playHistory.Count == 0)
            {
                _logger.LogDebug("SkipPrevious — nothing to go back to");
                return;
            }

            if (_nowPlaying is not null && Music.CurrentTime > TimeSpan.FromSeconds(3))
            {
                var current = _nowPlaying;
                try
                {
                    OpenQueueItemLocked(current);
                    Music.Play();
                    _pipeline.ResyncForNewTrack();
                    if (!_pipeline.IsTransmitting)
                        BeginTransmissionLocked();
                    changed = true;
                    _logger.LogInformation("SkipPrevious — restarted current track: {SourceKey}", current.SourceKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SkipPrevious — failed to restart {SourceKey}", current.SourceKey);
                }
            }
            else if (_playHistory.Count > 0)
            {
                var previousItem = _playHistory.Pop();
                var index = EnsureQueuedItemLocked(previousItem);
                PushCurrentToHistoryLocked();
                DemoteCurrentPlayingLocked();

                var target = _queue[index];
                target.Status = PlaybackQueueStatus.Playing;
                _nowPlaying = target;

                try
                {
                    OpenQueueItemLocked(target);
                    Music.Play();
                    if (!_pipeline.IsTransmitting)
                        BeginTransmissionLocked();
                    else
                        _pipeline.ResyncForNewTrack();
                    changed = true;
                    _logger.LogInformation("SkipPrevious — playing history item: {SourceKey}", previousItem.SourceKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SkipPrevious — failed to play history item: {SourceKey}", previousItem.SourceKey);
                    target.Status = PlaybackQueueStatus.Failed;
                    _nowPlaying = null;
                }
            }
        }

        if (changed)
        {
            RaiseNowPlayingChanged();
            RaiseQueueChanged();
        }
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
            DisplayName = item.DisplayName,
            Artist = item.Artist,
            Album = item.Album,
            DurationSeconds = item.DurationSeconds,
            Status = item.Status
        };

    public void Start()
    {
        lock (_sync)
        {
            if (_isPaused && Music.HasActiveTrack)
            {
                Music.ResumeOutput();
                BeginTransmissionLocked();
                _isPaused = false;
                _logger.LogInformation("Resumed paused music track: {SourceKey}", Music.CurrentFilePath);
                return;
            }

            if (Music.IsOutputting)
            {
                if (!_pipeline.IsTransmitting)
                {
                    _logger.LogInformation(
                        "Resuming voice transmission (transmitting={Transmitting})",
                        _pipeline.IsTransmitting);
                    BeginTransmissionLocked();
                }

                return;
            }

            if (!TryStartNextQueuedTrackLocked())
            {
                _logger.LogWarning("Start requested but no queued tracks available");
                return;
            }

            BeginTransmissionLocked();
        }

        if (Music.IsOutputting)
            _pipeline.ResyncForNewTrack();

        RaiseNowPlayingChanged();
        RaiseQueueChanged();
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (!Music.HasActiveTrack && !_isPaused)
                return;

            if (Music.HasActiveTrack)
                Music.PauseOutput();

            _isPaused = Music.HasActiveTrack;

            if (!HasActiveAudioLocked())
                StopTransmissionLocked("paused");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_nowPlaying is not null)
            {
                // A track stuck at EOF still reports IsPlaying; don't re-queue it ahead of later tracks.
                if (Music.IsStalled(StalledReadThreshold) || !Music.IsPlaying)
                    _nowPlaying.Status = PlaybackQueueStatus.Played;
                else
                    _nowPlaying.Status = PlaybackQueueStatus.Queued;

                _nowPlaying = null;
            }

            Music.Stop();
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

        RaiseNowPlayingChanged();
        RaiseQueueChanged();
    }

    private void HandleTrackFinishedLocked()
    {
        PlaybackQueueItem? finished = _nowPlaying;

        if (finished is not null)
        {
            finished.Status = PlaybackQueueStatus.Played;
            if (_playHistory.Count == 0 || _playHistory.Peek().SourceKey != finished.SourceKey)
                _playHistory.Push(CloneQueueItem(finished));
            _logger.LogInformation("Track finished: {SourceKey}", finished.SourceKey);
        }

        var queuedCount = _queue.Count(i => i.Status == PlaybackQueueStatus.Queued);
        foreach (var item in _queue)
        {
            _logger.LogInformation("  Queue item: {Name} [{Status}]", item.DisplayName, item.Status);
        }

        _logger.LogInformation(
            "Queue state after EOF: queued={QueuedCount}, played={PlayedCount}, total={Total}",
            queuedCount,
            _queue.Count(i => i.Status == PlaybackQueueStatus.Played),
            _queue.Count);

        if (queuedCount > 0)
        {
            _logger.LogInformation("Queue advancing to next track ({QueuedCount} remaining in queue)", queuedCount);

            if (!TryStartNextQueuedTrackLocked())
            {
                _logger.LogError("Queue advance failed despite {QueuedCount} queued items — entering idle", queuedCount);
                _nowPlaying = null;
                if (Soundboard.IsActive)
                {
                    if (!_pipeline.IsTransmitting)
                        BeginTransmissionLocked(forSoundboardOnly: true);
                }
                else
                    EnterIdleLocked("queue advance failed");
            }
            else
            {
                _pipeline.ResyncForNewTrack();
                if (!_pipeline.IsTransmitting)
                    BeginTransmissionLocked();
            }
        }
        else
        {
            _nowPlaying = null;
            if (Soundboard.IsActive)
            {
                if (!_pipeline.IsTransmitting)
                    BeginTransmissionLocked(forSoundboardOnly: true);
            }
            else
                EnterIdleLocked("queue finished");
        }

        RaiseNowPlayingChanged();
        RaiseQueueChanged();

        Music.NotifyTrackEndedEvent();
    }

    private bool TryStartNextQueuedTrackLocked()
    {
        var next = _queue.FirstOrDefault(item => item.Status == PlaybackQueueStatus.Queued);
        if (next is null)
        {
            _logger.LogWarning("TryStartNextQueuedTrack: no item with Queued status");
            return false;
        }

        foreach (var item in _queue)
        {
            if (item.Status == PlaybackQueueStatus.Playing)
                item.Status = PlaybackQueueStatus.Queued;
        }

        next.Status = PlaybackQueueStatus.Playing;
        _nowPlaying = next;

        try
        {
            OpenQueueItemLocked(next);
            Music.Play();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open/play queued track: {SourceKey}", next.SourceKey);
            next.Status = PlaybackQueueStatus.Queued;
            _nowPlaying = null;
            return false;
        }

        _logger.LogInformation(
            "Now playing: {SourceKey} (music.IsPlaying={IsPlaying}, pipeline.transmitting={Transmitting})",
            next.SourceKey, Music.IsPlaying, _pipeline.IsTransmitting);
        return true;
    }

    private void BeginTransmissionLocked(bool forSoundboardOnly = false)
    {
        _pipeline.ResetTimer();
        _pipeline.Start();
        if (!forSoundboardOnly)
            _isPaused = false;
        _logger.LogInformation(
            "Voice transmission started (music.outputting={IsOutputting}, soundboard.active={SoundboardActive}, soundboardOnly={SoundboardOnly})",
            Music.IsOutputting, Soundboard.IsActive, forSoundboardOnly);
    }

    private void StopTransmissionLocked(string reason)
    {
        _pipeline.Pause();
        _teamSpeak.VoiceTarget.LogTransmissionStopped(reason);
        _logger.LogInformation(
            "Voice transmission stopped ({Reason}) — pipeline paused, PreciseTimedPipe will stop requesting frames",
            reason);
    }

    private void EnterIdleLocked(string reason)
    {
        StopTransmissionLocked(reason);
        _isPaused = false;
        _logger.LogInformation(
            "Mixer idle ({Reason}) — music.IsPlaying={IsPlaying}, soundboard.active={SoundboardActive}, nowPlaying={NowPlaying}",
            reason, Music.IsPlaying, Soundboard.IsActive, _nowPlaying?.DisplayName ?? "none");
    }

    private bool HasActiveAudioLocked() =>
        Music.IsOutputting || Soundboard.IsActive;

    private void ProcessLifecycleLocked()
    {
        if (Music.TryConsumeTrackEnd(out var finishedPath))
        {
            _logger.LogInformation("ProcessLifecycle: EOF consumed for {SourceKey}", finishedPath);
            HandleTrackFinishedLocked();
            return;
        }

        if (Music.IsStalled(StalledReadThreshold))
        {
            _logger.LogWarning(
                "ProcessLifecycle: stalled reads while playing {SourceKey} — forcing track end",
                Music.CurrentFilePath);
            Music.ForceTrackEnd();
            return;
        }

        if (!HasActiveAudioLocked() && _pipeline.IsTransmitting)
        {
            _logger.LogInformation(
                "ProcessLifecycle: no active sources while pipeline still transmitting — forcing idle (music.outputting={MusicOutputting}, sfxLive={SfxCount}, channelAttached={Attached})",
                Music.IsOutputting, Soundboard.ActiveEffectCount, Soundboard.IsChannelAttached);
            EnterIdleLocked("no active sources during read");
        }
    }

    private void RaiseQueueChanged() => QueueChanged?.Invoke(this, EventArgs.Empty);

    private void RaiseNowPlayingChanged() => NowPlayingChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        Stop();
        Music.Dispose();
        Soundboard.Dispose();
        Microphone.Dispose();
        _pipeline.Dispose();
        _outputProducer.Dispose();
    }
}
