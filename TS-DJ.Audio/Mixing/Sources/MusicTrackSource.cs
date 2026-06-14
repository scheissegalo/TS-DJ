using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Decoding;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Mixing.Sources;

/// <summary>
/// Long-form music playback channel (future Deck A/B base).
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
    private float _volume = 1f;
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
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _channel.Volume = _volume;
        }
    }

    public event EventHandler? TrackEnded;

    public void Open(string filePath)
    {
        OpenPlaybackItem(PlaybackQueueItem.FromLocalFile(filePath));
    }

    public void OpenPlaybackItem(PlaybackQueueItem item, string? resolvedStreamUrl = null)
    {
        lock (_sync)
        {
            StopInternal(logStop: false);

            _decoder = new AudioFileDecoder();

            if (item.SourceKind == PlaybackSourceKind.LocalFile)
            {
                _decoder.Open(item.FilePath);
                _currentSourceKey = item.SourceKey;
            }
            else
            {
                var streamUrl = resolvedStreamUrl
                    ?? throw new InvalidOperationException("Remote stream URL is required.");
                var duration = item.DurationSeconds is > 0
                    ? TimeSpan.FromSeconds(item.DurationSeconds.Value)
                    : (TimeSpan?)null;
                _decoder.OpenUri(streamUrl, duration);
                _currentSourceKey = item.SourceKey;
            }

            _switchable.SetSource(_decoder.Output);
            RebindMixerInputLocked();
            _logger.LogInformation(
                "Music track opened: {SourceKey} (duration {Duration:F1}s)",
                _currentSourceKey, _decoder.TotalTime.TotalSeconds);
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
        {
            StopInternal(logStop: true);
        }
    }

    /// <summary>
    /// Returns true when EOF was reached on the last mixed read tick.
    /// Must be called from the mixer read path while holding the mixer lock.
    /// </summary>
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

    /// <summary>
    /// Returns true when a decode failure was signaled on the last mixed read tick.
    /// Must be called from the mixer read path while holding the mixer lock.
    /// </summary>
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

    /// <summary>
    /// Layer-2 fallback when a read exception escapes the decode boundary.
    /// Must be called while holding the mixer lock.
    /// </summary>
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

    /// <summary>
    /// NAudio's MixingSampleProvider drops inputs that return 0 samples (EOF/idle).
    /// Re-attach before each track so queue advances remain audible.
    /// </summary>
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
