using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Mixing.Sources;

/// <summary>
/// Microphone input scaffold — not attached to the mixer until capture is implemented.
/// </summary>
public sealed class MicrophoneSource : IMicrophoneSource
{
    private readonly SilenceSampleProvider _silence;
    private float _volume = 1f;
    private bool _capturing;

    public MicrophoneSource(string id, string name)
    {
        Id = id;
        Name = name;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
        _silence = new SilenceSampleProvider(format);
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsActive => _capturing;
    public bool IsCapturing => _capturing;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    internal ISampleProvider SampleProvider => _silence;

    /// <summary>
    /// WASAPI capture is not implemented yet.
    /// </summary>
    public void StartCapture()
    {
        throw new NotImplementedException(
            "Microphone capture is not implemented yet. WASAPI input will be added in a future phase.");
    }

    /// <summary>
    /// WASAPI capture is not implemented yet.
    /// </summary>
    public void StopCapture()
    {
        if (!_capturing)
            return;

        _capturing = false;
    }

    public void Dispose()
    {
    }
}
