using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Core.Audio;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// One-shot sample provider that marks itself finished when the underlying stream ends.
/// Cleanup must happen outside the mixer read path — NAudio removes zero-read inputs itself.
/// </summary>
internal sealed class OneShotSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public OneShotSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }
    public bool IsFinished { get; private set; }
    public IDisposable? Resource { get; init; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (IsFinished)
            return 0;

        var read = _source.Read(buffer, offset, count);
        if (read > 0)
            return read;

        IsFinished = true;
        return 0;
    }
}
