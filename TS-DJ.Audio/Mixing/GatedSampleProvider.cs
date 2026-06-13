using NAudio.Wave;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Returns silence when playback is stopped; forwards reads when playing.
/// When stopped, returns 0 samples so the mixer can become fully idle.
/// </summary>
internal sealed class GatedSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public GatedSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public bool IsPlaying { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!IsPlaying)
            return 0;

        return _source.Read(buffer, offset, count);
    }
}
