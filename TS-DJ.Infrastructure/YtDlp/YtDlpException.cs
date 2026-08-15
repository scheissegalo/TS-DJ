namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpException : Exception
{
    /// <summary>True when yt-dlp failed with HTTP 403 (transient, may succeed on retry).</summary>
    public bool IsHttp403 { get; }

    /// <summary>Raw stderr captured from the yt-dlp process (trimmed), when available.</summary>
    public string? Stderr { get; }

    public YtDlpException(string message) : base(message)
    {
    }

    public YtDlpException(string message, bool isHttp403 = false, string? stderr = null) : base(message)
    {
        IsHttp403 = isHttp403;
        Stderr = stderr;
    }

    public YtDlpException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
