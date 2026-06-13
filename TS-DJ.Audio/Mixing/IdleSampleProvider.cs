using NAudio.Wave;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Produces no samples — used for idle mixer channels so the pipeline can stop when inactive.
/// </summary>
internal sealed class IdleSampleProvider : ISampleProvider
{
    public IdleSampleProvider(WaveFormat waveFormat)
    {
        WaveFormat = waveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count) => 0;
}
