using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Decoding;
using TS_DJ.Core.Audio;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Fully decoded clip held in memory for low-latency one-shot playback.
/// </summary>
public sealed class PreloadedClip : IDisposable
{
    public const int MaxDurationSeconds = 30;
    public const int MaxSamples = AudioFormat.SampleRate * AudioFormat.Channels * MaxDurationSeconds;

    private readonly float[] _samples;

    public PreloadedClip(string filePath, float[] samples)
    {
        FilePath = filePath;
        _samples = samples;
    }

    public string FilePath { get; }

    public int SampleCount => _samples.Length;

    public static PreloadedClip? TryLoad(string filePath)
    {
        if (!AudioFileDecoder.IsSupportedFile(filePath) || !File.Exists(filePath))
            return null;

        using var decoder = new AudioFileDecoder();
        decoder.Open(filePath);

        if (decoder.TotalTime > TimeSpan.FromSeconds(MaxDurationSeconds))
            return null;

        var buffer = new float[AudioFormat.SampleRate * AudioFormat.Channels];
        var samples = new List<float>(buffer.Length);

        while (true)
        {
            var read = decoder.Output.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;

            if (samples.Count + read > MaxSamples)
                return null;

            for (var i = 0; i < read; i++)
                samples.Add(buffer[i]);
        }

        if (samples.Count == 0)
            return null;

        return new PreloadedClip(filePath, samples.ToArray());
    }

    internal BufferedClipSampleProvider CreateProvider() => new(_samples);

    public void Dispose()
    {
    }
}

/// <summary>
/// Plays a preloaded float buffer once, then marks finished.
/// </summary>
internal sealed class BufferedClipSampleProvider : ISampleProvider
{
    private readonly float[] _samples;
    private int _position;
    private bool _finished;

    public BufferedClipSampleProvider(float[] samples)
    {
        _samples = samples;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
    }

    public WaveFormat WaveFormat { get; }
    public bool IsFinished => _finished;

    public int Read(float[] buffer, int offset, int count)
    {
        if (_finished)
            return 0;

        var remaining = _samples.Length - _position;
        if (remaining <= 0)
        {
            _finished = true;
            return 0;
        }

        var toRead = Math.Min(count, remaining);
        Array.Copy(_samples, _position, buffer, offset, toRead);
        _position += toRead;

        if (_position >= _samples.Length)
            _finished = true;

        return toRead;
    }
}
