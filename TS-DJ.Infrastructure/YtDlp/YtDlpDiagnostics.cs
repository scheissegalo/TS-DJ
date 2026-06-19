using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpDiagnostics
{
    private readonly YtDlpLocator _locator;
    private readonly JsRuntimeLocator _jsRuntimeLocator;
    private readonly YtDlpProcessRunner _processRunner;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<YtDlpDiagnostics> _logger;

    private YoutubeDiagnosticsSnapshot _snapshot = new();

    public YtDlpDiagnostics(
        YtDlpLocator locator,
        JsRuntimeLocator jsRuntimeLocator,
        YtDlpProcessRunner processRunner,
        ISettingsService settingsService,
        ILogger<YtDlpDiagnostics> logger)
    {
        _locator = locator;
        _jsRuntimeLocator = jsRuntimeLocator;
        _processRunner = processRunner;
        _settingsService = settingsService;
        _logger = logger;
    }

    public YoutubeDiagnosticsSnapshot Current => _snapshot;

    public async Task<YoutubeDiagnosticsSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        _locator.InvalidateCache();
        _jsRuntimeLocator.InvalidateCache();

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

        _snapshot = new YoutubeDiagnosticsSnapshot
        {
            Status = status,
            YtDlpPath = location.Path,
            YtDlpOrigin = location.Origin.ToString(),
            YtDlpVersion = version,
            JsRuntimeStatus = jsDetection.StatusSummary,
            JsRuntimeName = jsDetection.SelectedRuntime,
            JsRuntimePath = jsDetection.SelectedPath,
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
