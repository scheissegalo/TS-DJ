using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer.NAudioSupport;
using TS_DJ.Core.Audio;

namespace TS_DJ.Audio.Decoding;

/// <summary>
/// Decodes local audio files to 48 kHz stereo IEEE float samples for the mixer.
/// </summary>
public sealed class AudioFileDecoder : IDisposable
{
    private WaveStream? _reader;
    private EofTrackingSampleProvider? _output;

    public ISampleProvider Output =>
        _output ?? throw new InvalidOperationException("No audio file is open.");

    public bool IsOpen => _output is not null;

    public bool ConsumeEofPending() => _output?.ConsumeEofPending() ?? false;

    internal void ForceEnd() => _output?.ForceEnd();

    public TimeSpan TotalTime => _reader?.TotalTime ?? TimeSpan.Zero;

    public static bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".aiff" or ".aif";
    }

    public void Open(string filePath)
    {
        DisposeReader();

        _reader = CreateReader(filePath);
        _output = CreateOutputProvider(_reader);

        // Log at Information level via caller; keep decoder free of ILogger dependency.
    }

    private static WaveStream CreateReader(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3" => CreateMp3Reader(filePath),
            ".wav" => new WaveFileReader(filePath),
            ".aiff" or ".aif" => new AiffFileReader(filePath),
            _ => throw new NotSupportedException(
                $"Unsupported audio format '{Path.GetExtension(filePath)}'. Use MP3, WAV, or AIFF.")
        };
    }

    private static WaveStream CreateMp3Reader(string filePath)
    {
        var builder = new Mp3FileReader.FrameDecompressorBuilder(wf => new Mp3FrameDecompressor(wf));
        return new Mp3FileReaderBase(filePath, builder);
    }

    private static EofTrackingSampleProvider CreateOutputProvider(WaveStream reader)
    {
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1)
            samples = new MonoToStereoSampleProvider(samples);

        var resampled = new WdlResamplingSampleProvider(samples, AudioFormat.SampleRate);
        return new EofTrackingSampleProvider(reader, resampled);
    }

    private void DisposeReader()
    {
        _output = null;
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose() => DisposeReader();
}
