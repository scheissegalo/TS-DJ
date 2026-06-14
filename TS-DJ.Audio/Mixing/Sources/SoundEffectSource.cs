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

    private readonly ILogger<SoundEffectSource> _logger;
    private readonly object _sync;
    private readonly MixingSampleProvider _mainMixer;
    private readonly MixerChannel _channel;
    private readonly MixingSampleProvider _effectMixer;
    private readonly List<OneShotSampleProvider> _activeEffects = [];
    private readonly PreloadedClipCache? _clipCache;
    private float _volume = 1f;

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
                return _activeEffects.Any(e => !e.IsFinished);
        }
    }

    internal int ActiveEffectCount
    {
        get
        {
            lock (_sync)
                return _activeEffects.Count(e => !e.IsFinished);
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

            if (_activeEffects.Count(e => !e.IsFinished) >= MaxConcurrentEffects)
            {
                _logger.LogWarning("Sound effect limit reached ({Limit}); dropping {FilePath}",
                    MaxConcurrentEffects, filePath);
                return;
            }

            RebindMixerInputLocked();

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

            _logger.LogInformation(
                "Sound effect added: {FilePath} (live={LiveCount}, attached={Attached})",
                filePath, _activeEffects.Count(e => !e.IsFinished), _channel.IsAttached);
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

    internal void TickCleanup()
    {
        lock (_sync)
        {
            var before = _activeEffects.Count;
            CleanupFinishedEffects();
            var removed = before - _activeEffects.Count;
            if (removed > 0)
            {
                _logger.LogDebug(
                    "Soundboard cleanup: removed {Removed} finished effect(s), live={LiveCount}, attached={Attached}",
                    removed, _activeEffects.Count(e => !e.IsFinished), _channel.IsAttached);
            }
        }
    }

    private void CleanupFinishedEffects()
    {
        for (var i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = _activeEffects[i];
            if (!effect.IsFinished)
                continue;

            _activeEffects.RemoveAt(i);
            effect.Resource?.Dispose();
            _logger.LogDebug("Sound effect disposed after EOF");
        }
    }

    /// <summary>
    /// NAudio drops mixer inputs that return 0 samples. Re-attach before each trigger.
    /// </summary>
    private void RebindMixerInputLocked()
    {
        _channel.Detach();
        _channel.Attach(_mainMixer);
        _logger.LogDebug("Soundboard mixer input re-bound to main mixer");
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
