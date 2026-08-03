namespace TS_DJ.App.Services;

public enum TeamSpeakCommandKind
{
    Unknown,
    Stop,
    Next,
    SetVolume,
    YouTubeVideo,
    YouTubePlaylist,
    Help
}

public sealed class TeamSpeakCommand
{
    public TeamSpeakCommandKind Kind { get; init; }
    public int Volume { get; init; }
    public string Url { get; init; } = string.Empty;

    public static TeamSpeakCommand Unknown { get; } = new() { Kind = TeamSpeakCommandKind.Unknown };
}

public static class TeamSpeakCommandParser
{
    public const char Prefix = '!';

    public static TeamSpeakCommand Parse(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return TeamSpeakCommand.Unknown;

        var trimmed = message.Trim();
        if (!trimmed.StartsWith(Prefix))
            return TeamSpeakCommand.Unknown;

        var body = trimmed[1..].TrimStart();
        if (body.Length == 0)
            return TeamSpeakCommand.Unknown;

        var spaceIndex = body.IndexOf(' ');
        var commandToken = spaceIndex >= 0 ? body[..spaceIndex] : body;
        var args = spaceIndex >= 0 ? body[(spaceIndex + 1)..].Trim() : string.Empty;

        return commandToken.ToLowerInvariant() switch
        {
            "stop" => new TeamSpeakCommand { Kind = TeamSpeakCommandKind.Stop },
            "next" => new TeamSpeakCommand { Kind = TeamSpeakCommandKind.Next },
            "volume" or "vol" => ParseVolume(args),
            "yt" => ParseUrlCommand(TeamSpeakCommandKind.YouTubeVideo, args),
            "ytp" => ParseUrlCommand(TeamSpeakCommandKind.YouTubePlaylist, args),
            "help" => new TeamSpeakCommand { Kind = TeamSpeakCommandKind.Help },
            _ => TeamSpeakCommand.Unknown
        };
    }

    private static TeamSpeakCommand ParseVolume(string args)
    {
        if (!int.TryParse(args, out var volume) || volume is < 1 or > 100)
            return TeamSpeakCommand.Unknown;

        return new TeamSpeakCommand
        {
            Kind = TeamSpeakCommandKind.SetVolume,
            Volume = volume
        };
    }

    private static TeamSpeakCommand ParseUrlCommand(TeamSpeakCommandKind kind, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return TeamSpeakCommand.Unknown;

        return new TeamSpeakCommand
        {
            Kind = kind,
            Url = TeamSpeakCommandUrlHelper.ExtractUrl(args)
        };
    }
}
