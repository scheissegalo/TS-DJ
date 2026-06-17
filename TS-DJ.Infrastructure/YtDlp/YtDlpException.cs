namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpException : Exception
{
    public YtDlpException(string message) : base(message)
    {
    }

    public YtDlpException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
