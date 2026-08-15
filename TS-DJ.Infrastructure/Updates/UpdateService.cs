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

        // Launch the updater from a shadow copy in the temp directory so the
        // running image never lives inside the install directory. Otherwise the
        // updater cannot overwrite its own executable (ETXTBSY on Linux, sharing
        // violation on Windows) and would abort mid-copy, corrupting the install.
        var shadowDir = Path.Combine(Path.GetTempPath(), "TS-DJ-update", $"updater-{Guid.NewGuid():N}");
        var shadowUpdater = Path.Combine(shadowDir, Path.GetFileName(updaterPath));
        try
        {
            Directory.CreateDirectory(shadowDir);
            foreach (var name in GetUpdaterCompanionFiles(updaterPath))
                File.Copy(Path.Combine(Path.GetDirectoryName(updaterPath)!, name), Path.Combine(shadowDir, name), overwrite: true);

            MakeExecutable(shadowUpdater);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not stage updater copy in {ShadowDir}", shadowDir);
            return Task.FromResult(false);
        }

        var arguments = string.Join(' ',
        [
            "--wait-pid", Environment.ProcessId.ToString(),
            "--install-dir", Quote(installDir),
            "--package", Quote(packagePath),
            "--restart", Quote(restartPath),
            "--cleanup-dir", Quote(shadowDir)
        ]);

        var startInfo = new ProcessStartInfo
        {
            FileName = shadowUpdater,
            Arguments = arguments,
            WorkingDirectory = installDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("Launching updater from {UpdaterPath} for package {PackagePath}", shadowUpdater, packagePath);
        Process.Start(startInfo);
        return Task.FromResult(true);
    }

    private static IEnumerable<string> GetUpdaterCompanionFiles(string updaterPath)
    {
        var directory = Path.GetDirectoryName(updaterPath)!;
        var fileName = Path.GetFileName(updaterPath);
        var baseName = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;

        foreach (var name in new[]
                 {
                     fileName,
                     $"{baseName}.dll",
                     $"{baseName}.runtimeconfig.json",
                     $"{baseName}.deps.json",
                     $"{baseName}.pdb"
                 })
        {
            if (File.Exists(Path.Combine(directory, name)))
                yield return name;
        }
    }

    private static void MakeExecutable(string path)
    {
        if (!File.Exists(path))
            return;

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
    }

    private static string Quote(string value) =>
        OperatingSystem.IsWindows() ? $"\"{value}\"" : value;
}
