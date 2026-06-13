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
    private readonly MixerChannel _channel;
    private readonly MixingSampleProvider _effectMixer;
    private readonly List<OneShotSampleProvider> _activeEffects = [];
    private float _volume = 1f;

    public SoundEffectSource(
        string id,
        string name,
        MixingSampleProvider mainMixer,
        object sync,
        ILogger<SoundEffectSource> logger)
    {
        Id = id;
        Name = name;
        _sync = sync;
        _logger = logger;

        var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
        _effectMixer = new MixingSampleProvider(format);
        _channel = new MixerChannel(_effectMixer);
        _channel.Attach(mainMixer);
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsActive => _activeEffects.Count > 0;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _channel.Volume = _volume;
        }
    }

    public void Play(string filePath)
    {
        lock (_sync)
        {
            CleanupFinishedEffects();

            if (_activeEffects.Count >= MaxConcurrentEffects)
            {
                _logger.LogWarning("Sound effect limit reached ({Limit}); dropping {FilePath}",
                    MaxConcurrentEffects, filePath);
                return;
            }

            var decoder = new AudioFileDecoder();
            decoder.Open(filePath);

            var oneShot = new OneShotSampleProvider(
                decoder.Output,
                RemoveEffect)
            {
                Resource = decoder
            };

            _effectMixer.AddMixerInput(oneShot);
            _activeEffects.Add(oneShot);
            _logger.LogDebug("Playing sound effect: {FilePath}", filePath);
        }
    }

    internal void TickCleanup()
    {
        lock (_sync)
        {
            CleanupFinishedEffects();
        }
    }

    private void RemoveEffect(OneShotSampleProvider effect)
    {
        lock (_sync)
        {
            _effectMixer.RemoveMixerInput(effect);
            _activeEffects.Remove(effect);
            effect.Resource?.Dispose();
        }
    }

    private void CleanupFinishedEffects()
    {
        for (var i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = _activeEffects[i];
            if (!effect.IsFinished)
                continue;

            _effectMixer.RemoveMixerInput(effect);
            _activeEffects.RemoveAt(i);
            effect.Resource?.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var effect in _activeEffects.ToArray())
            {
                _effectMixer.RemoveMixerInput(effect);
                effect.Resource?.Dispose();
            }

            _activeEffects.Clear();
            _channel.Detach();
        }
    }
}
