namespace TS_DJ.Infrastructure.YtDlp;

public enum YoutubeContentKind
{
    SingleVideo,
    Playlist
}

public sealed class YoutubeUrlClassification
{
    public required YoutubeContentKind Kind { get; init; }
    public string? VideoUrl { get; init; }
    public string? PlaylistUrl { get; init; }
    public string? PlaylistId { get; init; }
}
