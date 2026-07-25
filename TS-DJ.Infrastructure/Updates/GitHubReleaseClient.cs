using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TS_DJ.Core;
using TS_DJ.Core.Models;

namespace TS_DJ.Infrastructure.Updates;

public sealed class GitHubReleaseClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubReleaseClient> _logger;

    public GitHubReleaseClient(IHttpClientFactory httpClientFactory, ILogger<GitHubReleaseClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<UpdateReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(UpdateConstants.HttpClientName);
        var payload = await httpClient.GetFromJsonAsync<GitHubReleasePayload>(
            UpdateConstants.LatestReleaseApiUrl,
            cancellationToken);

        if (payload is null || string.IsNullOrWhiteSpace(payload.TagName))
        {
            _logger.LogWarning("GitHub latest release response was empty");
            return null;
        }

        if (!TryParseReleaseVersion(payload.TagName, out var version))
        {
            _logger.LogWarning("Unable to parse release tag {TagName}", payload.TagName);
            return null;
        }

        var assetName = AppVersion.GetExpectedAssetName(version);
        var asset = payload.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));

        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            _logger.LogWarning("Release {TagName} has no asset named {AssetName}", payload.TagName, assetName);
            return null;
        }

        return new UpdateReleaseInfo
        {
            Version = version,
            TagName = payload.TagName,
            ReleaseNotes = payload.Body?.Trim() ?? string.Empty,
            ReleasePageUrl = payload.HtmlUrl ?? string.Empty,
            DownloadUrl = asset.BrowserDownloadUrl,
            AssetName = asset.Name ?? assetName
        };
    }

    private static bool TryParseReleaseVersion(string tagName, out Version version)
    {
        var trimmed = tagName.StartsWith('v') ? tagName[1..] : tagName;
        return Version.TryParse(trimmed, out version!);
    }

    private sealed class GitHubReleasePayload
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset>? Assets { get; set; }
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
