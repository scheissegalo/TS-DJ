using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpCommandBuilder
{
    private readonly ISettingsService _settingsService;
    private readonly JsRuntimeLocator _jsRuntimeLocator;
    private readonly YtDlpProcessRunner _processRunner;

    public YtDlpCommandBuilder(
        ISettingsService settingsService,
        JsRuntimeLocator jsRuntimeLocator,
        YtDlpProcessRunner processRunner)
    {
        _settingsService = settingsService;
        _jsRuntimeLocator = jsRuntimeLocator;
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
        var format = string.IsNullOrWhiteSpace(settings.AudioFormatSelector)
            ? "bestaudio"
            : settings.AudioFormatSelector.Trim();

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
        return string.IsNullOrWhiteSpace(settings.AudioFormatSelector)
            ? "bestaudio"
            : settings.AudioFormatSelector.Trim();
    }

    private async Task<IReadOnlyList<string>> BuildCommonArgsAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadYtDlpSettingsAsync(cancellationToken);
        var detection = await _jsRuntimeLocator.DetectAsync(settings, _processRunner, cancellationToken);

        var args = new List<string>();
        args.AddRange(_jsRuntimeLocator.BuildJsRuntimeArgs(settings, detection));

        if (settings.EnableRemoteEjsComponents)
            args.AddRange(["--remote-components", "ejs:github"]);

        return args;
    }
}
