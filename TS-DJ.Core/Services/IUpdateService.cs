using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface IUpdateService
{
    Version CurrentVersion { get; }
    string CurrentVersionDisplay { get; }
    bool IsSupportedEnvironment { get; }

    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<string> DownloadUpdateAsync(UpdateReleaseInfo release, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    Task<bool> ApplyUpdateAsync(string packagePath, CancellationToken cancellationToken = default);
}
