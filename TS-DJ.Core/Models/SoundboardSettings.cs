namespace TS_DJ.Core.Models;

public sealed class SoundboardSettings
{
    public const string ConfigKey = "soundboard.config";
    public const int Columns = 3;
    public const int Rows = 4;
    public const int PadCount = Columns * Rows;

    public int SoundboardVolumeHuman { get; set; } = 50;
    public List<SoundboardPad> Pads { get; set; } = CreateDefaultPads();

    public static List<SoundboardPad> CreateDefaultPads()
    {
        var pads = new List<SoundboardPad>(PadCount);
        for (var i = 0; i < PadCount; i++)
        {
            pads.Add(new SoundboardPad
            {
                Index = i,
                Label = $"Pad {i + 1}",
                Hotkey = i < 12 ? $"F{i + 1}" : null
            });
        }

        return pads;
    }
}
