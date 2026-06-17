using TS_DJ.Core.Models;

namespace TS_DJ.TeamSpeak;

/// <summary>
/// Formats TeamSpeak client description text from playback metadata.
/// </summary>
public static class TeamSpeakDescriptionFormatter
{
    public const int MaxDescriptionLength = 200;

    public static string Format(PlaybackQueueItem? item, int? bitrateKbps = null)
    {
        if (item is null)
            return string.Empty;

        var lines = new List<string> { "Now Playing:" };

        var title = item.DisplayName?.Trim();
        var artist = item.Artist?.Trim();
        var album = item.Album?.Trim();

        if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
            lines.Add($"{artist} — {title}");
        else if (!string.IsNullOrWhiteSpace(title))
            lines.Add(title);
        else
            lines.Add("Unknown track");

        if (!string.IsNullOrWhiteSpace(album))
            lines.Add($"Album: {album}");

        var extras = BuildExtras(item, bitrateKbps);
        if (extras.Count > 0)
            lines.Add(string.Join(" · ", extras));

        var result = string.Join('\n', lines);
        return result.Length <= MaxDescriptionLength
            ? result
            : result[..MaxDescriptionLength];
    }

    private static List<string> BuildExtras(PlaybackQueueItem item, int? bitrateKbps)
    {
        var extras = new List<string>();

        if (item.DurationSeconds is > 0)
            extras.Add(FormatDuration(item.DurationSeconds.Value));

        if (bitrateKbps is > 0)
            extras.Add($"{bitrateKbps} kbps");

        extras.Add(item.SourceKind switch
        {
            PlaybackSourceKind.LocalFile => "Local",
            PlaybackSourceKind.YouTube => "YouTube",
            _ => "Navidrome"
        });

        return extras;
    }

    private static string FormatDuration(int totalSeconds)
    {
        var time = TimeSpan.FromSeconds(totalSeconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }
}
