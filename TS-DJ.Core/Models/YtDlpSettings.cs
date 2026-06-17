namespace TS_DJ.Core.Models;

public sealed class YtDlpSettings
{
    public const string ConfigKey = "ytdlp.config";

    public string ExecutablePath { get; set; } = string.Empty;
}
