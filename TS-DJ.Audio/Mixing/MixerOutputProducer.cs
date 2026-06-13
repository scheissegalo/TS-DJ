using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Core.Audio;
using TSLib.Audio;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Bridges NAudio mixed float samples to the TSLib byte-based passive producer interface.
/// Returns 0 when no sources are active so voice transmission stops.
/// </summary>
public sealed class MixerOutputProducer : IAudioPassiveProducer, ISampleInfo
{
    private readonly ISampleProvider _mixer;
    private readonly object _sync;
    private readonly IWaveProvider _waveProvider;
    private readonly Func<bool> _hasActiveAudio;
    private readonly Action _processLifecycle;
    private readonly Action? _afterRead;
    private readonly Action<int>? _onMixedRead;
    private readonly ILogger<MixerOutputProducer>? _logger;
    private byte[] _readBuffer = Array.Empty<byte>();
    private long _readCount;
    private long _zeroReadCount;
    private long _nonZeroReadCount;
    private bool _loggedIdleRead;

    public MixerOutputProducer(
        ISampleProvider mixer,
        object sync,
        Func<bool> hasActiveAudio,
        Action processLifecycle,
        Action? afterRead = null,
        Action<int>? onMixedRead = null,
        ILogger<MixerOutputProducer>? logger = null)
    {
        _mixer = mixer;
        _sync = sync;
        _hasActiveAudio = hasActiveAudio;
        _processLifecycle = processLifecycle;
        _afterRead = afterRead;
        _onMixedRead = onMixedRead;
        _logger = logger;
        _waveProvider = mixer.ToWaveProvider16();
    }

    public int SampleRate => AudioFormat.SampleRate;
    public int Channels => AudioFormat.Channels;
    public int BitsPerSample => AudioFormat.BitsPerSample;

    public int Read(byte[] buffer, int offset, int length, out Meta? meta)
    {
        meta = null;
        _readCount++;

        lock (_sync)
        {
            try
            {
                if (!_hasActiveAudio())
                {
                    _zeroReadCount++;
                    if (!_loggedIdleRead)
                    {
                        _logger?.LogInformation(
                            "MixerOutputProducer: idle — returning 0 bytes (read #{ReadCount}, active sources=false)",
                            _readCount);
                        _loggedIdleRead = true;
                    }

                    _afterRead?.Invoke();
                    _processLifecycle();
                    return 0;
                }

                _loggedIdleRead = false;

                if (_readBuffer.Length < length)
                    _readBuffer = new byte[length];

                var read = _waveProvider.Read(_readBuffer, 0, length);
                if (read > 0)
                {
                    _nonZeroReadCount++;
                    Buffer.BlockCopy(_readBuffer, 0, buffer, offset, read);
                    if (_nonZeroReadCount == 1 || _nonZeroReadCount % 500 == 0)
                    {
                        _logger?.LogDebug(
                            "MixerOutputProducer: read {Bytes} bytes (read #{ReadCount}, nonZero=#{NonZero})",
                            read, _readCount, _nonZeroReadCount);
                    }
                }
                else
                {
                    _zeroReadCount++;
                    _logger?.LogDebug(
                        "MixerOutputProducer: read 0 bytes while sources marked active (read #{ReadCount}, zero=#{Zero})",
                        _readCount, _zeroReadCount);
                }

                _onMixedRead?.Invoke(read);
                _afterRead?.Invoke();
                _processLifecycle();
                return read;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "MixerOutputProducer: exception during read #{ReadCount}", _readCount);
                throw;
            }
        }
    }

    public void Dispose()
    {
    }
}
