namespace TS_DJ.Core.Audio;

/// <summary>
/// Canonical internal audio format matching TSLib / KBTS3AudioBot pipeline.
/// </summary>
public static class AudioFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;
    public const int BytesPerSample = BitsPerSample / 8;

    /// <summary>Opus frame size: 960 samples = 20 ms at 48 kHz.</summary>
    public const int FrameSamples = 960;

    /// <summary>PCM bytes per Opus frame: 960 × 2 channels × 2 bytes.</summary>
    public const int FrameBytes = FrameSamples * Channels * BytesPerSample;

    public const int BytesPerSecond = SampleRate * Channels * BytesPerSample;
}
