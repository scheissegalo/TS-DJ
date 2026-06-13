using TS_DJ.Core.Audio;

namespace TS_DJ.Core.Models;

public sealed class AudioSettings
{
    public const string OpusBitrateKbpsKey = "audio.opus_bitrate_kbps";
    public const string MasterVolumeKey = "audio.master_volume";
    public const string MusicVolumeKey = "audio.music_volume";
    public const string SoundboardVolumeKey = "audio.soundboard_volume";

    public int OpusBitrateKbps { get; set; } = OpusBitratePresets.Default;
    public int MasterVolumeHuman { get; set; } = 50;
    public int MusicVolumeHuman { get; set; } = 50;
    public int SoundboardVolumeHuman { get; set; } = 50;
}
