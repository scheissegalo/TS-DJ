using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Decoding;
using TS_DJ.Core.Audio;
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
    private string? _currentFilePath;
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
    public string? CurrentFilePath => _currentFilePath;

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
        lock (_sync)
        {
            StopInternal(logStop: false);

            _decoder = new AudioFileDecoder();
            _decoder.Open(filePath);
            _switchable.SetSource(_decoder.Output);

            _currentFilePath = filePath;
            RebindMixerInputLocked();
            _logger.LogInformation(
                "Music track opened: {FilePath} (duration {Duration:F1}s)",
                filePath, _decoder.TotalTime.TotalSeconds);
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
            _logger.LogInformation("Music track started: {FilePath}", _currentFilePath);
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
            _logger.LogInformation("Music output paused: {FilePath}", _currentFilePath);
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
            _logger.LogInformation("Music output resumed: {FilePath}", _currentFilePath);
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

            finishedPath = _currentFilePath;
            StopInternal(logStop: false);
            _logger.LogInformation("Music track EOF reached: {FilePath}", finishedPath);
            return true;
        }
    }

    internal void ForceTrackEnd()
    {
        lock (_sync)
        {
            if (_decoder is null || !_gated.IsPlaying)
                return;

            _logger.LogWarning("Forcing track end: {FilePath}", _currentFilePath);
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
        if (logStop && _currentFilePath is not null)
            _logger.LogInformation("Music track stopped: {FilePath}", _currentFilePath);

        _trackActive = false;
        _gated.IsPlaying = false;
        _stalledReadCount = 0;
        _decoder?.Dispose();
        _decoder = null;
        _switchable.SetSource(new IdleSampleProvider(_format));
        _currentFilePath = null;
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
