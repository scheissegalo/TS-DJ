namespace TS_DJ.Core.Models;

public enum PlaybackQueueStatus
{
    Queued,
    Playing,
    Played,
    Failed
}

public sealed class PlaybackQueueItem
{
    public string FilePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public PlaybackQueueStatus Status { get; set; } = PlaybackQueueStatus.Queued;
}
