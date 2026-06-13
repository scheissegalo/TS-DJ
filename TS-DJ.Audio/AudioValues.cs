// Adapted from TS3AudioBot AudioValues.cs (OSL-3.0)
using TSLib.Helper;

namespace TS_DJ.Audio;

public static class AudioValues
{
    public const float MinVolume = 0;
    public const float MaxVolume = 100;

    // Reference: https://www.dr-lex.be/info-stuff/volumecontrols.html#table1
    // Adjusted values for 40dB
    private const float FactA = 1e-2f;
    private const float FactB = 4.61512f;

    public static float HumanVolumeToFactor(float value)
    {
        if (value < MinVolume) return 0;
        if (value > MaxVolume) return 1;

        value = (value - MinVolume) / (MaxVolume - MinVolume);
        return Tools.Clamp((float)(FactA * Math.Exp(FactB * value)) - FactA, 0, 1);
    }

    public static float FactorToHumanVolume(float value)
    {
        if (value < 0) return MinVolume;
        if (value > 1) return MaxVolume;

        value = Tools.Clamp((float)(Math.Log((value + FactA) / FactA) / FactB), 0, 1);
        return (value * (MaxVolume - MinVolume)) + MinVolume;
    }
}
