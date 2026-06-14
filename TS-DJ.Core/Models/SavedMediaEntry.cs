namespace TS_DJ.Core.Models;

public sealed class SavedMediaEntry
{
    public PlaybackSourceKind SourceKind { get; set; } = PlaybackSourceKind.LocalFile;
    public string FilePath { get; set; } = string.Empty;
    public string? RemoteTrackId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public int? DurationSeconds { get; set; }

    public static SavedMediaEntry FromQueueItem(PlaybackQueueItem item) =>
        new()
        {
            SourceKind = item.SourceKind,
            FilePath = item.FilePath,
            RemoteTrackId = item.RemoteTrackId,
            DisplayName = item.DisplayName,
            Artist = item.Artist,
            Album = item.Album,
            DurationSeconds = item.DurationSeconds
        };

    public PlaybackQueueItem ToQueueItem() =>
        new()
        {
            SourceKind = SourceKind,
            FilePath = FilePath,
            RemoteTrackId = RemoteTrackId,
            DisplayName = DisplayName,
            Artist = Artist,
            Album = Album,
            DurationSeconds = DurationSeconds,
            Status = PlaybackQueueStatus.Queued
        };
}
