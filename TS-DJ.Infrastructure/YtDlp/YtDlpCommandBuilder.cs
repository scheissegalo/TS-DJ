using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpCommandBuilder
{
    private readonly ISettingsService _settingsService;
    private readonly JsRuntimeLocator _jsRuntimeLocator;
    private readonly FfmpegLocator _ffmpegLocator;
    private readonly YtDlpProcessRunner _processRunner;

    public YtDlpCommandBuilder(
        ISettingsService settingsService,
        JsRuntimeLocator jsRuntimeLocator,
        FfmpegLocator ffmpegLocator,
        YtDlpProcessRunner processRunner)
    {
        _settingsService = settingsService;
        _jsRuntimeLocator = jsRuntimeLocator;
        _ffmpegLocator = ffmpegLocator;
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<string>> BuildVideoMetadataArgsAsync(
        string videoUrl,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        args.AddRange(await BuildCommonArgsAsync(cancellationToken));
        args.AddRange(["--dump-json", "--no-playlist", "--no-warnings", "--", videoUrl]);
        return args;
    }

    public async Task<IReadOnlyList<string>> BuildFlatPlaylistArgsAsync(
        string playlistUrl,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        args.AddRange(await BuildCommonArgsAsync(cancellationToken));
        args.AddRange(["--flat-playlist", "-J", "--no-warnings", "--", playlistUrl]);
        return args;
    }

    public async Task<IReadOnlyList<string>> BuildExtractMp3ArgsAsync(
        string videoUrl,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadYtDlpSettingsAsync(cancellationToken);
        var format = ResolveAudioFormatSelector(settings.AudioFormatSelector);

        var args = new List<string>();
        args.AddRange(await BuildCommonArgsAsync(cancellationToken));
        args.AddRange([
            "-f", format,
            "-x", "--audio-format", "mp3",
            "--audio-quality", "0",
            "--postprocessor-args", "ffmpeg:-ar 44100 -ac 2",
            "-o", outputPath,
            "--no-playlist",
            "--no-warnings",
            "--", videoUrl
        ]);
        return args;
    }

    public async Task<string> GetAudioFormatSelectorAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadYtDlpSettingsAsync(cancellationToken);
        return ResolveAudioFormatSelector(settings.AudioFormatSelector);
    }

    private async Task<IReadOnlyList<string>> BuildCommonArgsAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadYtDlpSettingsAsync(cancellationToken);
        var detection = await _jsRuntimeLocator.DetectAsync(settings, _processRunner, cancellationToken);

        var args = new List<string>();
        args.AddRange(_jsRuntimeLocator.BuildJsRuntimeArgs(settings, detection));

        var ffmpegLocation = _ffmpegLocator.GetYtDlpLocationArgument();
        if (!string.IsNullOrWhiteSpace(ffmpegLocation))
            args.AddRange(["--ffmpeg-location", ffmpegLocation]);

        args.AddRange(BuildCookieArgs(settings));

        if (settings.EnableRemoteEjsComponents)
            args.AddRange(["--remote-components", "ejs:github"]);

        return args;
    }

    internal static IReadOnlyList<string> BuildCookieArgs(YtDlpSettings settings)
    {
        switch (settings.CookieSource)
        {
            case YoutubeCookieSource.File:
            {
                var path = settings.CookieFilePath.Trim();
                if (string.IsNullOrWhiteSpace(path))
                    throw new YtDlpException(
                        "YouTube cookie file is enabled but no path is set. Configure it in Options → YouTube / yt-dlp.");

                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                    throw new YtDlpException($"YouTube cookie file not found: {fullPath}");

                return ["--cookies", fullPath];
            }
            case YoutubeCookieSource.Browser:
            {
                var browser = settings.CookiesFromBrowser.Trim();
                if (string.IsNullOrWhiteSpace(browser))
                    throw new YtDlpException(
                        "YouTube cookies-from-browser is enabled but no browser is set. Use firefox, chrome, chromium, brave, or edge.");

                return ["--cookies-from-browser", browser];
            }
            default:
                return [];
        }
    }

    internal static string ResolveAudioFormatSelector(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector) ||
            string.Equals(selector.Trim(), "bestaudio", StringComparison.OrdinalIgnoreCase))
            return YtDlpSettings.DefaultAudioFormatSelector;

        return selector.Trim();
    }
}
