namespace TS_DJ.Core.Models;

public enum PlaybackMode
{
    SequentialQueue,
    DualDeck
}

public sealed class PlaybackSettings
{
    public const string ConfigKey = "playback_settings";

    public PlaybackMode Mode { get; set; } = PlaybackMode.SequentialQueue;
    public bool CrossfadeEnabled { get; set; }
    public double CrossfadeDurationSeconds { get; set; } = 4.0;
}
