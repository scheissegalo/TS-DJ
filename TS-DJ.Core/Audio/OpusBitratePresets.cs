namespace TS_DJ.Core.Audio;

/// <summary>
/// Opus encoder bitrate presets (kilobits per second).
/// </summary>
public static class OpusBitratePresets
{
    public const int Low = 32;
    public const int Medium = 64;
    public const int High = 96;
    public const int VeryHigh = 128;

    public const int Default = Medium;

    public static readonly int[] All = [Low, Medium, High, VeryHigh];

    public static string Label(int kbps) => kbps switch
    {
        Low => "Low (32 kbps)",
        Medium => "Medium (64 kbps)",
        High => "High (96 kbps)",
        VeryHigh => "Very High (128 kbps)",
        _ => $"{kbps} kbps"
    };

    public static int Normalize(int kbps) =>
        All.Contains(kbps) ? kbps : Default;
}
