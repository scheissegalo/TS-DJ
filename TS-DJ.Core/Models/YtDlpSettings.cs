namespace TS_DJ.Core.Models;

public sealed class YtDlpSettings
{
    public const string ConfigKey = "ytdlp.config";

    /// <summary>
    /// Audio-only when YouTube offers it; otherwise a small muxed stream (needed when cookies
    /// switch the client to tv, which often has no standalone bestaudio).
    /// </summary>
    public const string DefaultAudioFormatSelector = "bestaudio/best[height<=360]/best";

    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Optional override path for the selected JS runtime binary or its directory.</summary>
    public string JsRuntimePath { get; set; } = string.Empty;

    public YoutubeJsRuntimePreference JsRuntime { get; set; } = YoutubeJsRuntimePreference.Auto;

    /// <summary>
    /// When enabled, yt-dlp may fetch EJS challenge solver scripts remotely (ejs:github).
    /// </summary>
    public bool EnableRemoteEjsComponents { get; set; } = true;

    /// <summary>yt-dlp format selector used for audio extraction.</summary>
    public string AudioFormatSelector { get; set; } = DefaultAudioFormatSelector;

    public YoutubeCookieSource CookieSource { get; set; } = YoutubeCookieSource.None;

    /// <summary>Netscape cookies.txt path, used when <see cref="CookieSource"/> is File.</summary>
    public string CookieFilePath { get; set; } = string.Empty;

    /// <summary>
    /// yt-dlp --cookies-from-browser value (e.g. firefox, chrome, chromium:Profile),
    /// used when <see cref="CookieSource"/> is Browser.
    /// </summary>
    public string CookiesFromBrowser { get; set; } = string.Empty;
}
