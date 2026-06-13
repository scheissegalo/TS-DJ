// Adapted from TS3AudioBot CustomTargetPipe.cs (OSL-3.0)
using TSLib;
using TSLib.Audio;
using TSLib.Full;

namespace TS_DJ.TeamSpeak;

/// <summary>
/// Routes encoded Opus packets to the TeamSpeak voice channel.
/// Phase 2 will wire this as the final sink in the audio pipeline.
/// </summary>
public sealed class Ts3VoiceTarget : IAudioPassiveConsumer
{
    private readonly TsFullClient _client;

    public Ts3VoiceTarget(TsFullClient client)
    {
        _client = client;
    }

    public bool Active => _client.Connected;

    public void Write(Span<byte> data, Meta? meta)
    {
        if (!Active)
            return;

        var codec = meta?.Codec ?? Codec.OpusMusic;
        _client.SendAudio(data, codec);
    }
}
