namespace TS_DJ.Core.Models;

public sealed class YoutubePlaylistEntry
{
    public required string VideoId { get; init; }
    public required string VideoUrl { get; init; }
    public string Title { get; init; } = "Unknown Title";
    public string? Uploader { get; init; }
    public int? DurationSeconds { get; init; }
}

public sealed class YoutubePlaylistMetadata
{
    public required string Title { get; init; }
    public required string PlaylistUrl { get; init; }
    public required IReadOnlyList<YoutubePlaylistEntry> Entries { get; init; }

    public int VideoCount => Entries.Count;
}
