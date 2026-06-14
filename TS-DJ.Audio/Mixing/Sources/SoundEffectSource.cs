using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Decoding;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Mixing.Sources;

/// <summary>
/// Soundboard channel supporting multiple overlapping short sounds.
/// </summary>
public sealed class SoundEffectSource : ISoundEffectSource
{
    private const int MaxConcurrentEffects = 8;
    private const int PumpBufferSamples = 4096;

    private readonly ILogger<SoundEffectSource> _logger;
    private readonly object _sync;
    private readonly MixingSampleProvider _mainMixer;
    private readonly MixerChannel _channel;
    private readonly MixingSampleProvider _effectMixer;
    private readonly List<OneShotSampleProvider> _activeEffects = [];
    private readonly float[] _pumpBuffer;
    private readonly PreloadedClipCache? _clipCache;
    private float _volume = 1f;
    private int _lastLoggedLiveCount = -1;

    public SoundEffectSource(
        string id,
        string name,
        MixingSampleProvider mainMixer,
        object sync,
        ILogger<SoundEffectSource> logger,
        PreloadedClipCache? clipCache = null)
    {
        Id = id;
        Name = name;
        _sync = sync;
        _mainMixer = mainMixer;
        _logger = logger;
        _clipCache = clipCache;
        _pumpBuffer = new float[PumpBufferSamples];

        var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
        _effectMixer = new MixingSampleProvider(format)
        {
            ReadFully = false
        };
        _channel = new MixerChannel(_effectMixer);
        _channel.Attach(mainMixer);
        _logger.LogInformation("Soundboard channel attached to main mixer");
    }

    public string Id { get; }
    public string Name { get; }

    public bool IsActive
    {
        get
        {
            lock (_sync)
                return CountLiveEffectsLocked() > 0;
        }
    }

    internal int ActiveEffectCount
    {
        get
        {
            lock (_sync)
                return CountLiveEffectsLocked();
        }
    }

    internal bool IsChannelAttached
    {
        get
        {
            lock (_sync)
                return _channel.IsAttached;
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

    public void Play(string filePath, float gainFactor = 1f)
    {
        lock (_sync)
        {
            CleanupFinishedEffects();
            EnsureChannelAttachedLocked();

            var liveCount = CountLiveEffectsLocked();
            if (liveCount >= MaxConcurrentEffects)
            {
                TryRecoverStuckEffectsLocked("play-saturated");

                liveCount = CountLiveEffectsLocked();
                if (liveCount >= MaxConcurrentEffects)
                {
                    _logger.LogWarning(
                        "Sound effect limit reached ({Limit}); dropping {FilePath} (live={LiveCount}, attached={Attached})",
                        MaxConcurrentEffects, filePath, liveCount, _channel.IsAttached);
                    return;
                }
            }

            OneShotSampleProvider oneShot;

            var cached = _clipCache?.Get(filePath);
            if (cached is not null)
            {
                ISampleProvider provider = cached.CreateProvider();
                provider = ApplyGain(provider, gainFactor);
                oneShot = new OneShotSampleProvider(provider);
                _logger.LogDebug("Playing cached sound effect: {FilePath} (gain={GainFactor:F2})", filePath, gainFactor);
            }
            else
            {
                var decoder = new AudioFileDecoder();
                decoder.Open(filePath);

                ISampleProvider provider = ApplyGain(decoder.Output, gainFactor);
                oneShot = new OneShotSampleProvider(provider)
                {
                    Resource = decoder
                };
                _logger.LogDebug("Playing sound effect (file decode): {FilePath} (gain={GainFactor:F2})", filePath, gainFactor);
            }

            _effectMixer.AddMixerInput(oneShot);
            _activeEffects.Add(oneShot);

            liveCount = CountLiveEffectsLocked();
            _logger.LogInformation(
                "Sound effect added: {FilePath} (live={LiveCount}, totalTracked={TrackedCount}, attached={Attached})",
                filePath, liveCount, _activeEffects.Count, _channel.IsAttached);
            LogLiveCountTransition(liveCount);
        }
    }

    private static ISampleProvider ApplyGain(ISampleProvider source, float gainFactor)
    {
        if (Math.Abs(gainFactor - 1f) < 0.0001f)
            return source;

        return new VolumeSampleProvider(source)
        {
            Volume = Math.Max(0f, gainFactor)
        };
    }

    /// <summary>
    /// Called after each mixer output read. Only pumps effects directly when the mixer
    /// produced no samples (detached/stuck path) to avoid double-consuming live audio.
    /// </summary>
    internal void TickCleanup(int mixerReadBytes)
    {
        lock (_sync)
        {
            var liveBefore = CountLiveEffectsLocked();
            var removed = CleanupFinishedEffects();
            EnsureChannelAttachedLocked();

            if (mixerReadBytes == 0 && CountLiveEffectsLocked() > 0)
                TryRecoverStuckEffectsLocked("tick-no-mixer-output");

            var liveAfter = CountLiveEffectsLocked();
            if (removed > 0 || (liveBefore != liveAfter && mixerReadBytes == 0))
            {
                _logger.LogDebug(
                    "Soundboard tick cleanup (mixerBytes={MixerBytes}): removed={Removed}, live {LiveBefore}->{LiveAfter}, attached={Attached}",
                    mixerReadBytes, removed, liveBefore, liveAfter, _channel.IsAttached);
            }

            if (liveBefore >= MaxConcurrentEffects && liveAfter < MaxConcurrentEffects)
            {
                _logger.LogInformation(
                    "Soundboard recovered after saturation (live {LiveBefore}->{LiveAfter}, attached={Attached})",
                    liveBefore, liveAfter, _channel.IsAttached);
            }

            LogLiveCountTransition(liveAfter);
        }
    }

    /// <summary>
    /// Advances orphaned one-shots when they are not being pulled through the main mixer.
    /// Must NOT run during normal playback — that would skip ahead and corrupt audio.
    /// </summary>
    private void TryRecoverStuckEffectsLocked(string reason)
    {
        var liveBefore = CountLiveEffectsLocked();
        if (liveBefore == 0)
            return;

        foreach (var effect in _activeEffects.ToArray())
        {
            if (effect.IsFinished)
                continue;

            _ = effect.Read(_pumpBuffer, 0, _pumpBuffer.Length);
        }

        var removed = CleanupFinishedEffects();
        var liveAfter = CountLiveEffectsLocked();

        if (removed > 0 || liveAfter < liveBefore)
        {
            _logger.LogDebug(
                "Soundboard stuck recovery ({Reason}): removed={Removed}, live {LiveBefore}->{LiveAfter}",
                reason, removed, liveBefore, liveAfter);
        }
    }

    private int CleanupFinishedEffects()
    {
        var removed = 0;

        for (var i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = _activeEffects[i];
            if (!effect.IsFinished)
                continue;

            _effectMixer.RemoveMixerInput(effect);
            _activeEffects.RemoveAt(i);
            effect.Resource?.Dispose();
            removed++;

            _logger.LogDebug(
                "Sound effect completed and removed (live={LiveCount}, tracked={TrackedCount})",
                CountLiveEffectsLocked(), _activeEffects.Count);
        }

        return removed;
    }

    private int CountLiveEffectsLocked() =>
        _activeEffects.Count(e => !e.IsFinished);

    private void LogLiveCountTransition(int liveCount)
    {
        if (liveCount == _lastLoggedLiveCount)
            return;

        _logger.LogInformation(
            "Soundboard live count: {LiveCount}/{Limit} (tracked={TrackedCount}, attached={Attached})",
            liveCount, MaxConcurrentEffects, _activeEffects.Count, _channel.IsAttached);
        _lastLoggedLiveCount = liveCount;
    }

    /// <summary>
    /// NAudio drops mixer inputs that return 0 samples. Re-register on each maintenance pass.
    /// </summary>
    private void EnsureChannelAttachedLocked()
    {
        _channel.Attach(_mainMixer);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var effect in _activeEffects.ToArray())
            {
                if (!effect.IsFinished)
                    _effectMixer.RemoveMixerInput(effect);

                effect.Resource?.Dispose();
            }

            _activeEffects.Clear();
            _channel.Detach();
            _logger.LogInformation("Soundboard channel detached and disposed");
        }
    }
}
