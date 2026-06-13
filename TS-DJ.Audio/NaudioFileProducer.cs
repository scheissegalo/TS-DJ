using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer.NAudioSupport;
using TS_DJ.Core.Audio;
using TSLib.Audio;

namespace TS_DJ.Audio;

/// <summary>
/// Decodes local audio files via NAudio and produces 48 kHz stereo signed 16-bit PCM
/// for the TSLib audio pipeline.
/// </summary>
public sealed class NaudioFileProducer : IAudioPassiveProducer, ISampleInfo
{
    private readonly ILogger<NaudioFileProducer> _logger;
    private WaveStream? _reader;
    private IWaveProvider? _output;
    private bool _ended;

    public NaudioFileProducer(ILogger<NaudioFileProducer> logger)
    {
        _logger = logger;
    }

    public int SampleRate => AudioFormat.SampleRate;
    public int Channels => AudioFormat.Channels;
    public int BitsPerSample => AudioFormat.BitsPerSample;

    public event EventHandler? SongEnded;

    public void Open(string filePath)
    {
        DisposeReader();

        _reader = CreateReader(filePath);
        _output = CreateOutputProvider(_reader);
        _ended = false;

        _logger.LogInformation(
            "Opened audio file {FilePath} ({SourceRate} Hz, {SourceChannels} ch) → {TargetRate} Hz stereo PCM",
            filePath,
            _reader.WaveFormat.SampleRate,
            _reader.WaveFormat.Channels,
            SampleRate);
    }

    public int Read(byte[] buffer, int offset, int length, out Meta? meta)
    {
        meta = null;

        if (_output is null || _ended)
            return 0;

        var read = _output.Read(buffer, offset, length);
        if (read > 0)
            return read;

        _ended = true;
        _logger.LogInformation("Audio file reached end");
        SongEnded?.Invoke(this, EventArgs.Empty);
        return 0;
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

    private static IWaveProvider CreateOutputProvider(WaveStream reader)
    {
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1)
            samples = new MonoToStereoSampleProvider(samples);

        var resampled = new WdlResamplingSampleProvider(samples, AudioFormat.SampleRate);
        return resampled.ToWaveProvider16();
    }

    private void DisposeReader()
    {
        _output = null;
        _reader?.Dispose();
        _reader = null;
        _ended = false;
    }

    public void Dispose() => DisposeReader();
}
