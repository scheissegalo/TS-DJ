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
    public PlaybackSourceKind SourceKind { get; init; } = PlaybackSourceKind.LocalFile;
    public string FilePath { get; init; } = string.Empty;
    public string? RemoteTrackId { get; init; }
    public string? VideoUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public int? DurationSeconds { get; init; }
    public PlaybackQueueStatus Status { get; set; } = PlaybackQueueStatus.Queued;

    /// <summary>
    /// Stable key for queue identity, play history, and logging (never contains passwords).
    /// </summary>
    public string SourceKey =>
        SourceKind switch
        {
            PlaybackSourceKind.LocalFile => FilePath,
            PlaybackSourceKind.YouTube => VideoUrl ?? DisplayName,
            _ => RemoteTrackId ?? DisplayName
        };

    public static PlaybackQueueItem FromLocalFile(string filePath, string? displayName = null) =>
        new()
        {
            SourceKind = PlaybackSourceKind.LocalFile,
            FilePath = filePath,
            DisplayName = displayName ?? Path.GetFileName(filePath),
            Status = PlaybackQueueStatus.Queued
        };
}

