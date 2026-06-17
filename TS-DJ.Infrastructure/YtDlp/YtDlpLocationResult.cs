namespace TS_DJ.Infrastructure.YtDlp;

public enum YtDlpLocationOrigin
{
    Configured,
    Bundled,
    PathEnvironment
}

public sealed class YtDlpLocationResult
{
    public required string Path { get; init; }
    public required YtDlpLocationOrigin Origin { get; init; }
}
