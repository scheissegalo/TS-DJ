using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Decoding;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Mixing.Sources;

/// <summary>
/// Long-form music playback channel (deck A/B base).
/// </summary>
public sealed class MusicTrackSource : IMusicTrackSource
{
    private readonly ILogger<MusicTrackSource> _logger;
    private readonly object _sync;
    private readonly MixingSampleProvider _mainMixer;
    private readonly MixerChannel _channel;
    private readonly SwitchableSampleProvider _switchable;
    private readonly GatedSampleProvider _gated;
    private readonly WaveFormat _format;
    private AudioFileDecoder? _decoder;
    private string? _currentSourceKey;
    private float _userVolume = 1f;
    private float _crossfadeGain = 1f;
    private bool _trackActive;

    public MusicTrackSource(
        string id,
        string name,
        MixingSampleProvider mixer,
        object sync,
        ILogger<MusicTrackSource> logger)
    {
        Id = id;
        Name = name;
        _sync = sync;
        _logger = logger;
        _mainMixer = mixer;
        _format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);

        _switchable = new SwitchableSampleProvider(new IdleSampleProvider(_format));
        _gated = new GatedSampleProvider(_switchable);
        _channel = new MixerChannel(_gated);
        _channel.Attach(mixer);
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsActive => IsPlaying;
    public bool IsPlaying => _trackActive;
    internal bool IsOutputting => _gated.IsPlaying;
    internal bool HasActiveTrack => _decoder is not null && _trackActive;
    internal MixerChannel MixerChannel => _channel;
    public string? CurrentFilePath => _currentSourceKey;

    public TimeSpan CurrentTime
    {
        get
        {
            lock (_sync)
                return _decoder?.CurrentTime ?? TimeSpan.Zero;
        }
    }

    public TimeSpan TotalTime
    {
        get
        {
            lock (_sync)
                return _decoder?.TotalTime ?? TimeSpan.Zero;
        }
    }

    public float Volume
    {
        get => _userVolume;
        set
        {
            _userVolume = Math.Clamp(value, 0f, 1f);
            ApplyOutputVolumeLocked();
        }
    }

    public event EventHandler? TrackEnded;

    public void Open(string filePath) =>
        Open(PlaybackQueueItem.FromLocalFile(filePath), MediaLoadResult.FromLocalFile(filePath));

    public void Open(PlaybackQueueItem item, MediaLoadResult loadResult)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(loadResult);

        lock (_sync)
        {
            StopInternal(logStop: false);
            _crossfadeGain = 1f;

            _decoder = new AudioFileDecoder();

            switch (loadResult.Kind)
            {
                case MediaLoadKind.LocalFile:
                    var path = loadResult.LocalFilePath ?? item.FilePath;
                    _decoder.Open(path);
                    _currentSourceKey = item.SourceKey;
                    break;

                case MediaLoadKind.RemoteStreamUrl:
                    var streamUrl = loadResult.StreamUrl
                        ?? throw new InvalidOperationException("Remote stream URL is required.");
                    var duration = item.DurationSeconds is > 0
                        ? TimeSpan.FromSeconds(item.DurationSeconds.Value)
                        : (TimeSpan?)null;
                    _decoder.OpenUri(streamUrl, duration);
                    _currentSourceKey = item.SourceKey;
                    break;

                case MediaLoadKind.BufferedStream:
                    var handle = loadResult.StreamHandle
                        ?? throw new InvalidOperationException("Buffered stream handle is required.");
                    var streamDuration = item.DurationSeconds is > 0
                        ? TimeSpan.FromSeconds(item.DurationSeconds.Value)
                        : (TimeSpan?)null;
                    _decoder.OpenStream(handle.OpenRead(), streamDuration);
                    _currentSourceKey = item.SourceKey;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(loadResult), loadResult.Kind, "Unsupported media load kind.");
            }

            _switchable.SetSource(_decoder.Output);
            RebindMixerInputLocked();
            ApplyOutputVolumeLocked();
            _logger.LogInformation(
                "Music track opened: {SourceKey} (duration {Duration:F1}s)",
                _currentSourceKey, _decoder.TotalTime.TotalSeconds);
        }
    }

    public void OpenPlaybackItem(PlaybackQueueItem item, string? resolvedStreamUrl = null)
    {
        if (item.SourceKind == PlaybackSourceKind.LocalFile)
            Open(item, MediaLoadResult.FromLocalFile(item.FilePath));
        else if (string.IsNullOrWhiteSpace(resolvedStreamUrl))
            throw new InvalidOperationException("Remote stream URL is required.");
        else
            Open(item, MediaLoadResult.FromRemoteUrl(resolvedStreamUrl));
    }

    public void OpenPlaybackItem(PlaybackQueueItem item, IPlaybackStreamHandle streamHandle)
    {
        ArgumentNullException.ThrowIfNull(streamHandle);
        Open(item, MediaLoadResult.FromBufferedStream(streamHandle));
    }

    public float CrossfadeGain
    {
        get
        {
            lock (_sync)
                return _crossfadeGain;
        }
    }

    internal void ResetCrossfadeGain()
    {
        lock (_sync)
        {
            _crossfadeGain = 1f;
            ApplyOutputVolumeLocked();
        }
    }

    internal void SetCrossfadeGain(float gain)
    {
        lock (_sync)
        {
            _crossfadeGain = Math.Clamp(gain, 0f, 1f);
            ApplyOutputVolumeLocked();
        }
    }

    public void Play()
    {
        lock (_sync)
        {
            if (_decoder is null)
                throw new InvalidOperationException("No track is open.");

            RebindMixerInputLocked();
            _trackActive = true;
            _gated.IsPlaying = true;
            _logger.LogInformation("Music track started: {SourceKey}", _currentSourceKey);
        }
    }

    public void Pause() => PauseOutput();

    internal void PauseOutput()
    {
        lock (_sync)
        {
            if (!_trackActive)
                return;

            _gated.IsPlaying = false;
            _stalledReadCount = 0;
            _logger.LogInformation("Music output paused: {SourceKey}", _currentSourceKey);
        }
    }

    internal void ResumeOutput()
    {
        lock (_sync)
        {
            if (!_trackActive || _decoder is null)
                return;

            RebindMixerInputLocked();
            _gated.IsPlaying = true;
            _logger.LogInformation("Music output resumed: {SourceKey}", _currentSourceKey);
        }
    }

    public void Stop()
    {
        lock (_sync)
            StopInternal(logStop: true);
    }

    public void Cue()
    {
        lock (_sync)
            StopInternal(logStop: true);
    }

    internal bool TryConsumeTrackEnd(out string? finishedPath)
    {
        lock (_sync)
        {
            finishedPath = null;

            if (_decoder is null || !_decoder.ConsumeEofPending())
                return false;

            finishedPath = _currentSourceKey;
            StopInternal(logStop: false);
            _logger.LogInformation("Music track EOF reached: {FilePath}", finishedPath);
            return true;
        }
    }

    internal bool TryConsumeTrackDecodeFailure(out string? failedSourceKey, out Exception? error)
    {
        lock (_sync)
        {
            failedSourceKey = null;
            error = null;

            if (_decoder is null || !_decoder.ConsumeDecodeFailurePending(out error))
                return false;

            failedSourceKey = _currentSourceKey;
            StopInternal(logStop: false);
            _logger.LogWarning(
                error,
                "Music track decode failure: {SourceKey} ({ExceptionType}) — source removed, mixer survives",
                failedSourceKey,
                error?.GetType().Name ?? "Unknown");
            return true;
        }
    }

    internal void AbortTrackDueToDecodeError(Exception ex)
    {
        lock (_sync)
        {
            if (_decoder is null || !_gated.IsPlaying)
                return;

            _logger.LogWarning(
                ex,
                "Aborting track due to mixer read exception: {SourceKey} ({ExceptionType})",
                _currentSourceKey,
                ex.GetType().Name);
            _decoder.SignalDecodeFailure(ex);
        }
    }

    internal void ForceTrackEnd()
    {
        lock (_sync)
        {
            if (_decoder is null || !_gated.IsPlaying)
                return;

            _logger.LogWarning("Forcing track end: {SourceKey}", _currentSourceKey);
            _decoder.ForceEnd();
        }
    }

    internal void NotifyMixedRead(int sampleBytes)
    {
        if (sampleBytes > 0)
            _stalledReadCount = 0;
        else if (_gated.IsPlaying)
            _stalledReadCount++;
    }

    internal bool IsStalled(int threshold) =>
        _gated.IsPlaying && _stalledReadCount >= threshold;

    private int _stalledReadCount;

    internal void NotifyTrackEndedEvent()
    {
        _logger.LogDebug("TrackEnded event firing");
        TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void StopInternal(bool logStop)
    {
        if (logStop && _currentSourceKey is not null)
            _logger.LogInformation("Music track stopped: {SourceKey}", _currentSourceKey);

        _trackActive = false;
        _gated.IsPlaying = false;
        _stalledReadCount = 0;
        _decoder?.Dispose();
        _decoder = null;
        _switchable.SetSource(new IdleSampleProvider(_format));
        _currentSourceKey = null;
    }

    private void ApplyOutputVolumeLocked() =>
        _channel.Volume = _userVolume * _crossfadeGain;

    private void RebindMixerInputLocked()
    {
        _channel.Detach();
        _channel.Attach(_mainMixer);
        _logger.LogDebug("Music mixer input re-bound");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            StopInternal(logStop: false);
            _channel.Detach();
            _logger.LogInformation("Music source removed from mixer");
        }
    }
}
