namespace TS_DJ.Infrastructure.Updates;

internal static class UpdateConstants
{
    public const string RepositoryOwner = "scheissegalo";
    public const string RepositoryName = "TS-DJ";
    public const string LatestReleaseApiUrl =
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
    public const string HttpClientName = "GitHubReleases";
}
