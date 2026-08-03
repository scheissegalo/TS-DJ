using System.Text.RegularExpressions;

namespace TS_DJ.App.Services;

/// <summary>
/// Extracts plain HTTP(S) URLs from TeamSpeak chat text (BBCode links, trailing punctuation).
/// </summary>
internal static partial class TeamSpeakCommandUrlHelper
{
    [GeneratedRegex(@"\[URL=(https?://[^\]\s]+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex TeamSpeakUrlWithLabelRegex();

    [GeneratedRegex(@"\[URL\](https?://[^\[]+)\[/URL\]", RegexOptions.IgnoreCase)]
    private static partial Regex TeamSpeakUrlRegex();

    [GeneratedRegex(@"https?://[^\s\]\)>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex PlainUrlRegex();

    public static string ExtractUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var trimmed = input.Trim();

        var labeled = TeamSpeakUrlWithLabelRegex().Match(trimmed);
        if (labeled.Success)
            return TrimTrailingJunk(labeled.Groups[1].Value);

        var wrapped = TeamSpeakUrlRegex().Match(trimmed);
        if (wrapped.Success)
            return TrimTrailingJunk(wrapped.Groups[1].Value);

        var plain = PlainUrlRegex().Match(trimmed);
        if (plain.Success)
            return TrimTrailingJunk(plain.Value);

        return TrimTrailingJunk(trimmed);
    }

    private static string TrimTrailingJunk(string url)
    {
        return url.TrimEnd(']', ')', '>', ',', '.', ';', '"', '\'');
    }
}
