using TS_DJ.Core.Audio;

namespace TS_DJ.Core.Models;

public sealed class AudioSettings
{
    public const string OpusBitrateKbpsKey = "audio.opus_bitrate_kbps";

    public int OpusBitrateKbps { get; set; } = OpusBitratePresets.Default;
}
