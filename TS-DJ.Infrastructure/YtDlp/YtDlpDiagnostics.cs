using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpDiagnostics
{
    private readonly YtDlpLocator _locator;
    private readonly JsRuntimeLocator _jsRuntimeLocator;
    private readonly FfmpegLocator _ffmpegLocator;
    private readonly YtDlpProcessRunner _processRunner;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<YtDlpDiagnostics> _logger;

    private YoutubeDiagnosticsSnapshot _snapshot = new();

    public YtDlpDiagnostics(
        YtDlpLocator locator,
        JsRuntimeLocator jsRuntimeLocator,
        FfmpegLocator ffmpegLocator,
        YtDlpProcessRunner processRunner,
        ISettingsService settingsService,
        ILogger<YtDlpDiagnostics> logger)
    {
        _locator = locator;
        _jsRuntimeLocator = jsRuntimeLocator;
        _ffmpegLocator = ffmpegLocator;
        _processRunner = processRunner;
        _settingsService = settingsService;
        _logger = logger;
    }

    public YoutubeDiagnosticsSnapshot Current => _snapshot;

    public async Task<YoutubeDiagnosticsSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        _locator.InvalidateCache();
        _jsRuntimeLocator.InvalidateCache();
        _ffmpegLocator.InvalidateCache();

        var location = await _locator.LocateAsync(cancellationToken);
        if (location is null)
        {
            _snapshot = new YoutubeDiagnosticsSnapshot
            {
                Status = YoutubeDiagnosticsStatus.NotFound,
                StatusMessage = "yt-dlp executable not found."
            };
            return _snapshot;
        }

        string? version = null;
        try
        {
            version = (await _processRunner.RunCaptureStdoutAsync(
                location.Path,
                ["--version"],
                TimeSpan.FromSeconds(15),
                cancellationToken)).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read yt-dlp version from {Path}", location.Path);
        }

        var settings = await _settingsService.LoadYtDlpSettingsAsync(cancellationToken);
        var jsDetection = await _jsRuntimeLocator.DetectAsync(settings, _processRunner, cancellationToken);
        var ffmpegLocation = _ffmpegLocator.Locate();

        var status = YoutubeDiagnosticsStatus.Ready;
        string? statusMessage = null;

        if (string.IsNullOrWhiteSpace(version))
        {
            status = YoutubeDiagnosticsStatus.Error;
            statusMessage = "yt-dlp found but version check failed.";
        }
        else if (jsDetection.SelectedRuntime is null && settings.JsRuntime != YoutubeJsRuntimePreference.None)
        {
            status = YoutubeDiagnosticsStatus.Degraded;
            statusMessage = "No JS runtime detected. YouTube playback may fail on some videos.";
        }
        else if (ffmpegLocation is null)
        {
            status = YoutubeDiagnosticsStatus.Degraded;
            statusMessage = "ffmpeg not found. YouTube MP3 extraction may fail.";
        }

        _snapshot = new YoutubeDiagnosticsSnapshot
        {
            Status = status,
            YtDlpPath = location.Path,
            YtDlpOrigin = location.Origin.ToString(),
            YtDlpVersion = version,
            JsRuntimeStatus = jsDetection.StatusSummary,
            JsRuntimeName = jsDetection.SelectedRuntime,
            JsRuntimePath = jsDetection.SelectedPath,
            FfmpegPath = ffmpegLocation?.Path,
            FfmpegOrigin = ffmpegLocation?.Origin.ToString(),
            StatusMessage = statusMessage
        };

        return _snapshot;
    }

    public async Task LogStartupAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await RefreshAsync(cancellationToken);

        if (snapshot.Status == YoutubeDiagnosticsStatus.NotFound)
        {
            _logger.LogWarning("YouTube integration unavailable: yt-dlp not found");
            return;
        }

        _logger.LogInformation(
            "YouTube / yt-dlp ready — path={Path}, origin={Origin}, version={Version}",
            snapshot.YtDlpPath,
            snapshot.YtDlpOrigin,
            snapshot.YtDlpVersion ?? "unknown");

        _logger.LogInformation("YouTube JS runtime: {JsRuntimeStatus}", snapshot.JsRuntimeStatus);

        if (snapshot.FfmpegPath is not null)
        {
            _logger.LogInformation(
                "YouTube ffmpeg: path={Path}, origin={Origin}",
                snapshot.FfmpegPath,
                snapshot.FfmpegOrigin ?? "unknown");
        }
        else
        {
            _logger.LogWarning("YouTube ffmpeg not found (bundled or PATH)");
        }

        if (snapshot.Status == YoutubeDiagnosticsStatus.Degraded)
            _logger.LogWarning("YouTube integration degraded: {Message}", snapshot.StatusMessage);
    }

    public void LogPlaybackAttempt(
        string videoId,
        string format,
        string commandLine,
        bool success,
        Exception? failure = null)
    {
        if (success)
        {
            _logger.LogInformation(
                "YouTube playback resolved — videoId={VideoId}, format={Format}, command={Command}",
                videoId,
                format,
                commandLine);
            return;
        }

        _logger.LogWarning(
            failure,
            "YouTube playback failed — videoId={VideoId}, format={Format}, command={Command}",
            videoId,
            format,
            commandLine);
    }
}
