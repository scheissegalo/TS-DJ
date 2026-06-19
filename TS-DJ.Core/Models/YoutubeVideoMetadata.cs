namespace TS_DJ.Core.Models;

public sealed class YoutubeVideoMetadata
{
    public required string VideoId { get; init; }
    public required string Title { get; init; }
    public string? Uploader { get; init; }
    public int? DurationSeconds { get; init; }
    public string? ThumbnailUrl { get; init; }
    public required string WebpageUrl { get; init; }
}
