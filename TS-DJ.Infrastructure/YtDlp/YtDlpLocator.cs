using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpLocator
{
    private readonly ISettingsService _settingsService;
    private readonly YtDlpProcessRunner _processRunner;
    private readonly ILogger<YtDlpLocator> _logger;
    private YtDlpLocationResult? _cached;

    public YtDlpLocator(
        ISettingsService settingsService,
        YtDlpProcessRunner processRunner,
        ILogger<YtDlpLocator> logger)
    {
        _settingsService = settingsService;
        _processRunner = processRunner;
        _logger = logger;
    }

    public void InvalidateCache() => _cached = null;

    public async Task<YtDlpLocationResult?> LocateAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null && File.Exists(_cached.Path))
            return _cached;

        var settings = await _settingsService.LoadYtDlpSettingsAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.ExecutablePath))
        {
            var configured = Path.GetFullPath(settings.ExecutablePath.Trim());
            if (await ValidateExecutableAsync(configured, cancellationToken))
            {
                _cached = new YtDlpLocationResult { Path = configured, Origin = YtDlpLocationOrigin.Configured };
                _logger.LogInformation("Using configured yt-dlp executable: {Path}", configured);
                return _cached;
            }

            _logger.LogWarning("Configured yt-dlp path is invalid or not executable: {Path}", configured);
        }

        foreach (var candidate in GetBundledCandidates())
        {
            if (await ValidateExecutableAsync(candidate, cancellationToken))
            {
                _cached = new YtDlpLocationResult { Path = candidate, Origin = YtDlpLocationOrigin.Bundled };
                _logger.LogInformation("Using bundled yt-dlp executable: {Path}", candidate);
                return _cached;
            }
        }

        var pathEnv = await FindOnPathAsync(cancellationToken);
        if (pathEnv is not null)
        {
            _cached = new YtDlpLocationResult { Path = pathEnv, Origin = YtDlpLocationOrigin.PathEnvironment };
            _logger.LogInformation("Using yt-dlp from PATH: {Path}", pathEnv);
            return _cached;
        }

        _logger.LogWarning("yt-dlp executable not found (configured, bundled, or PATH)");
        _cached = null;
        return null;
    }

    private static IEnumerable<string> GetBundledCandidates()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "tools", "yt-dlp", "yt-dlp");
        yield return Path.GetFullPath(Path.Combine(baseDir, "..", "tools", "yt-dlp", "yt-dlp"));
        yield return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "tools", "yt-dlp", "yt-dlp"));
    }

    private static async Task<string?> FindOnPathAsync(CancellationToken cancellationToken)
    {
        var name = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVar))
            return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
                return candidate;
        }

        if (OperatingSystem.IsWindows())
            return null;

        try
        {
            var output = await new YtDlpProcessRunner().RunCaptureStdoutAsync(
                "which",
                ["yt-dlp"],
                TimeSpan.FromSeconds(5),
                cancellationToken);

            var resolved = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }
        catch
        {
            return null;
        }
    }

    private Task<bool> ValidateExecutableAsync(string path, CancellationToken cancellationToken) =>
        _processRunner.ValidateExecutableAsync(path, TimeSpan.FromSeconds(15), cancellationToken);
}
