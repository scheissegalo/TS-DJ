namespace TS_DJ.Core.Models;

public sealed class UpdateCheckResult
{
    public required bool IsSupportedEnvironment { get; init; }
    public required Version CurrentVersion { get; init; }
    public UpdateReleaseInfo? AvailableUpdate { get; init; }
    public string? ErrorMessage { get; init; }

    public bool HasUpdate =>
        AvailableUpdate is not null && AvailableUpdate.Version > CurrentVersion;

    public bool IsUpToDate =>
        IsSupportedEnvironment && AvailableUpdate is not null && !HasUpdate;

    public static UpdateCheckResult Unsupported(Version currentVersion, string? reason = null) =>
        new()
        {
            IsSupportedEnvironment = false,
            CurrentVersion = currentVersion,
            ErrorMessage = reason
        };

    public static UpdateCheckResult Failed(Version currentVersion, string errorMessage) =>
        new()
        {
            IsSupportedEnvironment = true,
            CurrentVersion = currentVersion,
            ErrorMessage = errorMessage
        };
}
