using TSLib;
using TSLib.Audio;
using TSLib.Helper;
using TSLib.Scheduler;

namespace TS_DJ.Audio;

/// <summary>
/// Audio pipeline for streaming to TeamSpeak.
/// Source → PreciseTimedPipe → CheckActivePipe → VolumePipe → EncoderPipe → Ts3VoiceTarget
/// </summary>
public sealed class AudioPipeline : IDisposable
{
    private const Codec SendCodec = Codec.OpusMusic;
    private const int DefaultBitrate = 48000;

    public CheckActivePipe CheckActivePipe { get; }
    public VolumePipe VolumePipe { get; }
    public EncoderPipe EncoderPipe { get; }
    public PreciseTimedPipe TimePipe { get; }

    public IAudioPassiveProducer? Source { get; private set; }

    public AudioPipeline(Id id)
    {
        CheckActivePipe = new CheckActivePipe();
        VolumePipe = new VolumePipe();
        EncoderPipe = new EncoderPipe(SendCodec) { Bitrate = DefaultBitrate };
        TimePipe = new PreciseTimedPipe(EncoderPipe, id) { ReadBufferSize = EncoderPipe.PacketSize };

        TimePipe.Chain(CheckActivePipe).Chain(VolumePipe).Chain(EncoderPipe);
    }

    public void SetSource(IAudioPassiveProducer source)
    {
        Source = source;
        source.Into(TimePipe);
    }

    public void SetTarget(IAudioPassiveConsumer target)
    {
        EncoderPipe.Chain(target);
    }

    public void SetVolume(float humanVolume)
    {
        VolumePipe.Volume = AudioValues.HumanVolumeToFactor(humanVolume);
    }

    public void Start() => TimePipe.Paused = false;

    public void Pause() => TimePipe.Paused = true;

    public void ResetTimer() => TimePipe.AudioTimer.Reset();

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
    }
}
