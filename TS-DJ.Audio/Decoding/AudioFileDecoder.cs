using NAudio.Flac;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer.NAudioSupport;
using TS_DJ.Core.Audio;

namespace TS_DJ.Audio.Decoding;

/// <summary>
/// Decodes local audio files and remote HTTP streams to 48 kHz stereo IEEE float samples for the mixer.
/// </summary>
public sealed class AudioFileDecoder : IDisposable
{
    private WaveStream? _reader;
    private EofTrackingSampleProvider? _output;
    private TimeSpan? _knownDuration;

    public ISampleProvider Output =>
        _output ?? throw new InvalidOperationException("No audio source is open.");

    public bool IsOpen => _output is not null;

    public bool ConsumeEofPending() => _output?.ConsumeEofPending() ?? false;

    public bool ConsumeDecodeFailurePending(out Exception? error)
    {
        if (_output is null)
        {
            error = null;
            return false;
        }

        return _output.ConsumeDecodeFailurePending(out error);
    }

    internal void SignalDecodeFailure(Exception ex) => _output?.SignalDecodeFailure(ex);

    internal Exception? LastReadException => _output?.LastReadException;

    internal void ForceEnd() => _output?.ForceEnd();

    public TimeSpan TotalTime
    {
        get
        {
            if (_knownDuration is { } known && known > TimeSpan.Zero)
                return known;

            return _reader?.TotalTime ?? TimeSpan.Zero;
        }
    }

    public TimeSpan CurrentTime => _output?.CurrentTime ?? TimeSpan.Zero;

    public static bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".aiff" or ".aif" or ".flac";
    }

    public static bool IsRemoteUri(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public void Open(string filePath)
    {
        DisposeReader();
        _knownDuration = null;

        _reader = CreateReader(filePath);
        _output = CreateOutputProvider(_reader, null);
    }

    public void OpenUri(string uri, TimeSpan? knownDuration = null)
    {
        if (!IsRemoteUri(uri))
            throw new ArgumentException("Remote source must be an HTTP or HTTPS URL.", nameof(uri));

        DisposeReader();
        _knownDuration = knownDuration;

        try
        {
            // MP3 decoder needs a seekable stream (ID3 tag handling). Buffer this track only.
            var seekable = Task.Run(() => RemoteAudioHttp.DownloadAsSeekableStream(uri))
                .GetAwaiter()
                .GetResult();

            _reader = CreateMp3Reader(seekable);
            _output = CreateOutputProvider(_reader, knownDuration);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or IOException)
        {
            throw new IOException("Failed to open remote audio stream.", ex);
        }
    }

    public void OpenStream(Stream stream, TimeSpan? knownDuration = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        DisposeReader();
        _knownDuration = knownDuration;

        _reader = CreateReaderFromStream(stream);
        _output = CreateOutputProvider(_reader, knownDuration);
    }

    private static WaveStream CreateReaderFromStream(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        var header = new byte[4];
        var read = stream.Read(header, 0, header.Length);
        stream.Position = 0;

        if (read >= 3 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
            return CreateMp3Reader(stream);

        if (read >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return CreateMp3Reader(stream);

        if (read >= 4 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F')
            return new WaveFileReader(stream);

        if (read >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            throw new NotSupportedException(
                "Received WebM audio from yt-dlp instead of MP3. FFmpeg extraction may have failed.");
        }

        return CreateMp3Reader(stream);
    }

    private static WaveStream CreateReader(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3" => CreateMp3Reader(filePath),
            ".wav" => new WaveFileReader(filePath),
            ".aiff" or ".aif" => new AiffFileReader(filePath),
            ".flac" => new FlacReader(filePath),
            _ => throw new NotSupportedException(
                $"Unsupported audio format '{Path.GetExtension(filePath)}'. Use MP3, WAV, AIFF, or FLAC.")
        };
    }

    private static WaveStream CreateMp3Reader(string filePath)
    {
        var builder = new Mp3FileReader.FrameDecompressorBuilder(wf => new Mp3FrameDecompressor(wf));
        return new Mp3FileReaderBase(filePath, builder);
    }

    private static WaveStream CreateMp3Reader(Stream stream)
    {
        var builder = new Mp3FileReader.FrameDecompressorBuilder(wf => new Mp3FrameDecompressor(wf));
        return new Mp3FileReaderBase(stream, builder);
    }

    private static EofTrackingSampleProvider CreateOutputProvider(WaveStream reader, TimeSpan? knownDuration)
    {
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1)
            samples = new MonoToStereoSampleProvider(samples);

        var resampled = new WdlResamplingSampleProvider(samples, AudioFormat.SampleRate);
        return new EofTrackingSampleProvider(reader, resampled, knownDuration);
    }

    private void DisposeReader()
    {
        _output = null;
        _knownDuration = null;
        _reader?.Dispose();
        _reader = null;
    }

    public void Dispose() => DisposeReader();
}

