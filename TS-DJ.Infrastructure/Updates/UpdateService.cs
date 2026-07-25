using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TS_DJ.Core;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Updates;

public sealed class UpdateService : IUpdateService
{
    private readonly GitHubReleaseClient _releaseClient;
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(
        GitHubReleaseClient releaseClient,
        IHttpClientFactory httpClientFactory,
        ILogger<UpdateService> logger)
    {
        _releaseClient = releaseClient;
        _httpClient = httpClientFactory.CreateClient(UpdateConstants.HttpClientName);
        _logger = logger;
    }

    public Version CurrentVersion => AppVersion.Current;

    public string CurrentVersionDisplay => AppVersion.Display;

    public bool IsSupportedEnvironment =>
        !AppVersion.IsDevelopmentBuild
        && File.Exists(AppVersion.GetUpdaterPath(AppContext.BaseDirectory))
        && !string.IsNullOrWhiteSpace(Environment.ProcessPath);

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupportedEnvironment)
        {
            _logger.LogDebug("Update checks disabled for this environment");
            return UpdateCheckResult.Unsupported(CurrentVersion);
        }

        try
        {
            var latest = await _releaseClient.GetLatestReleaseAsync(cancellationToken);
            if (latest is null)
                return UpdateCheckResult.Failed(CurrentVersion, "Could not read the latest release from GitHub.");

            return new UpdateCheckResult
            {
                IsSupportedEnvironment = true,
                CurrentVersion = CurrentVersion,
                AvailableUpdate = latest
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return UpdateCheckResult.Failed(CurrentVersion, ex.Message);
        }
    }

    public async Task<string> DownloadUpdateAsync(
        UpdateReleaseInfo release,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var downloadDir = Path.Combine(Path.GetTempPath(), "TS-DJ-update");
        Directory.CreateDirectory(downloadDir);
        var destinationPath = Path.Combine(downloadDir, release.AssetName);

        if (File.Exists(destinationPath))
            File.Delete(destinationPath);

        using var response = await _httpClient.GetAsync(
            release.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = File.Create(destinationPath);

        if (totalBytes is null or <= 0)
        {
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
            progress?.Report(1);
            return destinationPath;
        }

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            progress?.Report(totalRead / (double)totalBytes.Value);
        }

        return destinationPath;
    }

    public Task<bool> ApplyUpdateAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!IsSupportedEnvironment)
            return Task.FromResult(false);

        var installDir = AppContext.BaseDirectory;
        var updaterPath = AppVersion.GetUpdaterPath(installDir);
        var restartPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(restartPath) || !File.Exists(restartPath))
        {
            _logger.LogError("Restart executable not found");
            return Task.FromResult(false);
        }

        if (!File.Exists(updaterPath))
        {
            _logger.LogError("Updater executable not found at {UpdaterPath}", updaterPath);
            return Task.FromResult(false);
        }

        var arguments = string.Join(' ',
        [
            "--wait-pid", Environment.ProcessId.ToString(),
            "--install-dir", Quote(installDir),
            "--package", Quote(packagePath),
            "--restart", Quote(restartPath)
        ]);

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            Arguments = arguments,
            WorkingDirectory = installDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("Launching updater for package {PackagePath}", packagePath);
        Process.Start(startInfo);
        return Task.FromResult(true);
    }

    private static string Quote(string value) =>
        OperatingSystem.IsWindows() ? $"\"{value}\"" : value;
}
