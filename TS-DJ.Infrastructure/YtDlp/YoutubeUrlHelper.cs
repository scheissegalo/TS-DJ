using System.Text.RegularExpressions;

namespace TS_DJ.Infrastructure.YtDlp;

public static partial class YoutubeUrlHelper
{
    [GeneratedRegex(@"^(?:https?://)?(?:www\.|m\.)?youtube\.com/watch\?", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeWatchRegex();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?music\.youtube\.com/watch\?", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeMusicWatchRegex();

    [GeneratedRegex(@"^(?:https?://)?youtu\.be/", RegexOptions.IgnoreCase)]
    private static partial Regex YouTubeShortRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]{11}$")]
    private static partial Regex VideoIdRegex();

    public static bool TryNormalize(string input, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;

        if (!IsYouTubeHost(uri.Host))
            return false;

        if (!TryExtractVideoId(trimmed, out var videoId))
            return false;

        if (IsPlaylistOnly(trimmed, videoId))
            return false;

        normalizedUrl = $"https://www.youtube.com/watch?v={videoId}";
        return true;
    }

    public static bool IsYouTubeUrl(string input) =>
        TryNormalize(input, out _);

    private static bool IsYouTubeHost(string host)
    {
        host = host.ToLowerInvariant();
        return host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtu.be";
    }

    private static bool IsPlaylistOnly(string url, string videoId)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var query = ParseQuery(uri.Query);
        return query.TryGetValue("list", out var list) && !string.IsNullOrWhiteSpace(list)
               && !query.ContainsKey("v") && videoId.Length == 0;
    }

    private static bool TryExtractVideoId(string url, out string videoId)
    {
        videoId = string.Empty;

        if (YouTubeShortRegex().IsMatch(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            videoId = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? string.Empty;
            return VideoIdRegex().IsMatch(videoId);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;

        if (YouTubeWatchRegex().IsMatch(url) || YouTubeMusicWatchRegex().IsMatch(url))
        {
            var query = ParseQuery(parsed.Query);
            videoId = query.GetValueOrDefault("v") ?? string.Empty;
            return VideoIdRegex().IsMatch(videoId);
        }

        return false;
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
