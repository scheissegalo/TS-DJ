using Microsoft.Extensions.Logging;
using TSLib;
using TSLib.Audio;
using TSLib.Helper;
using TS_DJ.Core.Audio;

namespace TS_DJ.Audio;

/// <summary>
/// Audio pipeline for streaming to TeamSpeak.
/// Source → PreciseTimedPipe → CheckActivePipe → VolumePipe → EncoderPipe → Ts3VoiceTarget
/// </summary>
public sealed class AudioPipeline : IDisposable
{
    private const Codec SendCodec = Codec.OpusMusic;
    private readonly ILogger? _logger;

    public CheckActivePipe CheckActivePipe { get; }
    public VolumePipe VolumePipe { get; }
    public EncoderPipe EncoderPipe { get; }
    public PreciseTimedPipe TimePipe { get; }

    public IAudioPassiveProducer? Source { get; private set; }
    public bool IsTransmitting => !TimePipe.Paused;

    public AudioPipeline(Id id, ILogger? logger = null)
    {
        _logger = logger;

        CheckActivePipe = new CheckActivePipe();
        VolumePipe = new VolumePipe();
        EncoderPipe = new EncoderPipe(SendCodec) { Bitrate = OpusBitratePresets.Default * 1000 };
        TimePipe = new PreciseTimedPipe(EncoderPipe, id) { ReadBufferSize = EncoderPipe.PacketSize };
        TimePipe.OnReadException = ex =>
            _logger?.LogError(ex, "PreciseTimedPipe read thread exception — thread survives, paused={Paused}", TimePipe.Paused);

        TimePipe.Chain(CheckActivePipe).Chain(VolumePipe).Chain(EncoderPipe);
    }

    public void SetSource(IAudioPassiveProducer source)
    {
        Source = source;
        source.Into(TimePipe);
        _logger?.LogDebug("AudioPipeline source connected to PreciseTimedPipe");
    }

    public void SetTarget(IAudioPassiveConsumer target)
    {
        EncoderPipe.Chain(target);
        _logger?.LogDebug("AudioPipeline encoder connected to voice target");
    }

    public void SetMasterVolume(float humanVolume)
    {
        VolumePipe.Volume = AudioValues.HumanVolumeToFactor(humanVolume);
    }

    public void SetEncoderBitrate(int kbps)
    {
        var bps = OpusBitratePresets.Normalize(kbps) * 1000;
        EncoderPipe.Bitrate = bps;
        _logger?.LogInformation("EncoderPipe bitrate set to {BitrateBps} bps ({BitrateKbps} kbps)", bps, kbps);
    }

    public int EncoderBitrateKbps => EncoderPipe.Bitrate / 1000;

    public void Start()
    {
        TimePipe.Paused = false;
        _logger?.LogInformation("PreciseTimedPipe started — will request audio frames from source");
    }

    public void Pause()
    {
        TimePipe.Paused = true;
        _logger?.LogInformation("PreciseTimedPipe paused — will stop requesting audio frames");
    }

    public void ResetTimer() => TimePipe.AudioTimer.Reset();

    public void ResyncForNewTrack()
    {
        if (TimePipe.Paused)
            ResetTimer();
        else
            TimePipe.AudioTimer.ResetRemoteBuffer();

        _logger?.LogInformation("Pipeline timer resynced for new track");
    }

    public void ClearSource()
    {
        Source?.Dispose();
        Source = null;
        TimePipe.InStream = null;
    }

    public void Dispose()
    {
        ClearSource();
        TimePipe.Dispose();
        EncoderPipe.Dispose();
    }
}
