using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Core.Audio;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Wraps a sample provider with per-channel volume and mixer attach/detach helpers.
/// </summary>
public sealed class MixerChannel
{
    private readonly VolumeSampleProvider _volumeProvider;
    private MixingSampleProvider? _mixer;

    public MixerChannel(ISampleProvider source)
    {
        _volumeProvider = new VolumeSampleProvider(source)
        {
            Volume = 1f
        };
    }

    public ISampleProvider SampleProvider => _volumeProvider;

    public float Volume
    {
        get => _volumeProvider.Volume;
        set => _volumeProvider.Volume = Math.Clamp(value, 0f, 1f);
    }

    public bool IsAttached => _mixer is not null;

    public void Attach(MixingSampleProvider mixer)
    {
        // Always re-register: NAudio may silently remove inputs that returned 0 samples
        // while _mixer still points at the parent mixer (stale IsAttached).
        if (_mixer is not null)
            _mixer.RemoveMixerInput(_volumeProvider);

        _mixer = mixer;
        _mixer.AddMixerInput(_volumeProvider);
    }

    public void Detach()
    {
        if (_mixer is null)
            return;

        _mixer.RemoveMixerInput(_volumeProvider);
        _mixer = null;
    }
}
