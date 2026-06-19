using System.Text.RegularExpressions;

namespace TS_DJ.Infrastructure.YtDlp;

public static partial class YoutubeUrlHelper
{
    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?youtube\.com/watch\?", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeWatchRegex();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?youtube\.com/playlist\?", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubePlaylistRegex();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?music\.youtube\.com/watch\?", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeMusicWatchRegex();

    [GeneratedRegex(@"^(?:https?://)?youtu\.be/", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeShortRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]{11}$")]
    private static partial Regex VideoIdRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
    private static partial Regex PlaylistIdRegex();

    public static bool IsRecognizedUrl(string input) =>
        TryClassify(input, out _);

    public static bool IsYouTubeUrl(string input) =>
        TryClassify(input, out var c) && c.Kind == YoutubeContentKind.SingleVideo;

    public static bool TryClassify(string input, out YoutubeUrlClassification classification)
    {
        classification = null!;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        if (!IsYouTubeHost(uri.Host))
            return false;

        var query = ParseQuery(uri.Query);

        if (query.TryGetValue("list", out var playlistId) && !string.IsNullOrWhiteSpace(playlistId)
            && PlaylistIdRegex().IsMatch(playlistId))
        {
            TryExtractVideoId(trimmed, query, out var videoId);
            classification = new YoutubeUrlClassification
            {
                Kind = YoutubeContentKind.Playlist,
                PlaylistId = playlistId,
                PlaylistUrl = BuildPlaylistFetchUrl(playlistId, videoId),
                VideoUrl = VideoIdRegex().IsMatch(videoId) ? BuildVideoUrl(videoId) : null
            };
            return true;
        }

        if (YouTubePlaylistRegex().IsMatch(trimmed))
        {
            if (!query.TryGetValue("list", out var playlistOnlyId) || string.IsNullOrWhiteSpace(playlistOnlyId))
                return false;

            classification = new YoutubeUrlClassification
            {
                Kind = YoutubeContentKind.Playlist,
                PlaylistId = playlistOnlyId,
                PlaylistUrl = BuildPlaylistFetchUrl(playlistOnlyId, videoId: null)
            };
            return true;
        }

        if (TryExtractVideoId(trimmed, query, out var singleVideoId))
        {
            classification = new YoutubeUrlClassification
            {
                Kind = YoutubeContentKind.SingleVideo,
                VideoUrl = BuildVideoUrl(singleVideoId)
            };
            return true;
        }

        return false;
    }

    public static bool TryNormalize(string input, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (!TryClassify(input, out var classification))
            return false;

        if (classification.Kind != YoutubeContentKind.SingleVideo
            || string.IsNullOrWhiteSpace(classification.VideoUrl))
        {
            return false;
        }

        normalizedUrl = classification.VideoUrl;
        return true;
    }

    public static bool TryNormalizePlaylistUrl(string input, out string playlistUrl)
    {
        playlistUrl = string.Empty;
        if (!TryClassify(input, out var classification))
            return false;

        if (classification.Kind != YoutubeContentKind.Playlist
            || string.IsNullOrWhiteSpace(classification.PlaylistUrl))
        {
            return false;
        }

        playlistUrl = classification.PlaylistUrl;
        return true;
    }

    public static string BuildVideoUrl(string videoId) =>
        $"https://www.youtube.com/watch?v={videoId}";

    public static string BuildWatchPlaylistUrl(string videoId, string playlistId) =>
        $"https://www.youtube.com/watch?v={videoId}&list={playlistId}";

    public static string BuildPlaylistUrl(string playlistId) =>
        $"https://www.youtube.com/playlist?list={playlistId}";

    /// <summary>
    /// URL passed to yt-dlp. Mix/radio playlists (RD/LL/…) require watch+list form;
    /// standard playlists work with either, but watch+list is used when a seed video is known.
    /// </summary>
    public static string BuildPlaylistFetchUrl(string playlistId, string? videoId)
    {
        if (!string.IsNullOrWhiteSpace(videoId) && VideoIdRegex().IsMatch(videoId))
            return BuildWatchPlaylistUrl(videoId, playlistId);

        return BuildPlaylistUrl(playlistId);
    }

    public static bool RequiresWatchContext(string playlistId) =>
        playlistId.StartsWith("RD", StringComparison.OrdinalIgnoreCase)
        || playlistId.StartsWith("LL", StringComparison.OrdinalIgnoreCase)
        || playlistId.StartsWith("WL", StringComparison.OrdinalIgnoreCase)
        || playlistId.StartsWith("LM", StringComparison.OrdinalIgnoreCase);

    private static bool IsYouTubeHost(string host)
    {
        host = host.ToLowerInvariant();
        return host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtu.be";
    }

    private static bool TryExtractVideoId(string url, IReadOnlyDictionary<string, string> query, out string videoId)
    {
        videoId = string.Empty;

        if (YouTubeShortRegex().IsMatch(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            videoId = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? string.Empty;
            return VideoIdRegex().IsMatch(videoId);
        }

        if (YouTubeWatchRegex().IsMatch(url) || YouTubeMusicWatchRegex().IsMatch(url))
        {
            videoId = query.GetValueOrDefault("v") ?? string.Empty;
            return VideoIdRegex().IsMatch(videoId);
        }

        return false;
    }

    private static bool TryExtractVideoId(string url, out string videoId)
    {
        videoId = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return TryExtractVideoId(url, ParseQuery(uri.Query), out videoId);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return result;

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = Uri.UnescapeDataString(part[..eq]);
            var value = Uri.UnescapeDataString(part[(eq + 1)..]);
            result[key] = value;
        }

        return result;
    }
}
