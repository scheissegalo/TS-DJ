namespace TS_DJ.Core.Models;

public sealed class SoundboardPad
{
    public int Index { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string? Hotkey { get; set; }
    public int GainHuman { get; set; } = 100;
}
