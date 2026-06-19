namespace TS_DJ.Core.Models;

public sealed class YtDlpSettings
{
    public const string ConfigKey = "ytdlp.config";

    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Optional override path for the selected JS runtime binary or its directory.</summary>
    public string JsRuntimePath { get; set; } = string.Empty;

    public YoutubeJsRuntimePreference JsRuntime { get; set; } = YoutubeJsRuntimePreference.Auto;

    /// <summary>
    /// When enabled, yt-dlp may fetch EJS challenge solver scripts remotely (ejs:github).
    /// </summary>
    public bool EnableRemoteEjsComponents { get; set; } = true;

    /// <summary>yt-dlp format selector used for audio extraction (default: bestaudio).</summary>
    public string AudioFormatSelector { get; set; } = "bestaudio";
}
