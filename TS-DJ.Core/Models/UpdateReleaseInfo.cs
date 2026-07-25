namespace TS_DJ.Core.Models;

public sealed class UpdateReleaseInfo
{
    public required Version Version { get; init; }
    public required string TagName { get; init; }
    public required string ReleaseNotes { get; init; }
    public required string ReleasePageUrl { get; init; }
    public required string DownloadUrl { get; init; }
    public required string AssetName { get; init; }
}
