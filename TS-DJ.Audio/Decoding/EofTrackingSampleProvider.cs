using NAudio.Wave;

namespace TS_DJ.Audio.Decoding;

/// <summary>
/// Wraps a resampled stream and signals EOF using duration, stream position,
/// and resampler drain detection.
/// </summary>
internal sealed class EofTrackingSampleProvider : ISampleProvider
{
    private const int MaxPostExhaustionReads = 4;

    private readonly WaveStream _reader;
    private readonly ISampleProvider _resampled;
    private readonly long _expectedSamples;
    private bool _eof;
    private bool _eofPending;
    private bool _hasDeliveredAudio;
    private bool _sourceExhausted;
    private int _postExhaustionReads;
    private int _consecutiveZeroReads;
    private long _deliveredSamples;

    public EofTrackingSampleProvider(WaveStream reader, ISampleProvider resampled)
    {
        _reader = reader;
        _resampled = resampled;
        WaveFormat = resampled.WaveFormat;

        _expectedSamples = reader.TotalTime > TimeSpan.Zero
            ? (long)(reader.TotalTime.TotalSeconds * WaveFormat.SampleRate * WaveFormat.Channels)
            : -1;
    }

    public WaveFormat WaveFormat { get; }

    public bool ConsumeEofPending()
    {
        if (!_eofPending)
            return false;

        _eofPending = false;
        return true;
    }

    internal void ForceEnd() => MarkEof();

    public int Read(float[] buffer, int offset, int count)
    {
        if (_eof)
            return 0;

        if (!_sourceExhausted && IsSourceExhausted())
            _sourceExhausted = true;

        var read = _resampled.Read(buffer, offset, count);

        if (read > 0)
        {
            _consecutiveZeroReads = 0;
            _hasDeliveredAudio = true;
            _deliveredSamples += read;

            if (HasReachedDurationEnd())
            {
                MarkEof();
                return read;
            }

            if (_sourceExhausted)
            {
                _postExhaustionReads++;
                if (_postExhaustionReads >= MaxPostExhaustionReads)
                    MarkEof();
            }

            return read;
        }

        _consecutiveZeroReads++;
        if (_sourceExhausted || HasReachedDurationEnd()
            || (_hasDeliveredAudio && _consecutiveZeroReads >= 4))
        {
            MarkEof();
        }

        return 0;
    }

    private bool HasReachedDurationEnd()
    {
        if (_expectedSamples <= 0)
            return false;

        return _deliveredSamples >= _expectedSamples;
    }

    private void MarkEof()
    {
        _eof = true;
        _eofPending = true;
    }

    private bool IsSourceExhausted()
    {
        if (_reader.Length > 0 && _reader.Position >= _reader.Length)
            return true;

        if (_reader.TotalTime > TimeSpan.Zero && _reader.CurrentTime >= _reader.TotalTime)
            return true;

        return false;
    }
}
