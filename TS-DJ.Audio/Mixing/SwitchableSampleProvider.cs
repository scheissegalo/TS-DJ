using NAudio.Wave;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Sample provider whose underlying source can be swapped at runtime.
/// </summary>
internal sealed class SwitchableSampleProvider : ISampleProvider
{
    private ISampleProvider _current;

    public SwitchableSampleProvider(ISampleProvider initial)
    {
        _current = initial;
        WaveFormat = initial.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public void SetSource(ISampleProvider source)
    {
        _current = source;
    }

    public int Read(float[] buffer, int offset, int count) =>
        _current.Read(buffer, offset, count);
}
