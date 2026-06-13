using NAudio.Wave;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Produces continuous silence at the target format.
/// </summary>
internal sealed class SilenceSampleProvider : ISampleProvider
{
    public SilenceSampleProvider(WaveFormat waveFormat)
    {
        WaveFormat = waveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        return count;
    }
}
