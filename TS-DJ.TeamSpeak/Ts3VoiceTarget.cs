using Microsoft.Extensions.Logging;
using TSLib;
using TSLib.Audio;
using TSLib.Full;

namespace TS_DJ.TeamSpeak;

/// <summary>
/// Routes encoded Opus packets to TeamSpeak with transmission tracing.
/// </summary>
public sealed class Ts3VoiceTarget : IAudioPassiveConsumer
{
    private readonly TsFullClient _client;
    private readonly ILogger? _logger;
    private long _packetsSent;
    private long _bytesSent;

    public Ts3VoiceTarget(TsFullClient client, ILogger? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    public bool Active => _client.Connected;

    public void Write(Span<byte> data, Meta? meta)
    {
        if (!Active || data.IsEmpty)
            return;

        _packetsSent++;
        _bytesSent += data.Length;

        if (_packetsSent == 1 || _packetsSent % 250 == 0)
        {
            _logger?.LogDebug(
                "Voice packet sent #{PacketCount} ({TotalBytes} bytes cumulative)",
                _packetsSent, _bytesSent);
        }

        var codec = meta?.Codec ?? Codec.OpusMusic;
        _client.SendAudio(data, codec);
    }

    public void LogTransmissionStopped(string reason)
    {
        if (_packetsSent == 0)
            return;

        _logger?.LogInformation(
            "Voice transmission ended ({Reason}) — {PacketCount} packets, {TotalBytes} bytes sent",
            reason, _packetsSent, _bytesSent);

        _packetsSent = 0;
        _bytesSent = 0;
    }
}
