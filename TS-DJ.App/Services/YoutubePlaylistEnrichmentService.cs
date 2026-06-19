using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.Media;
using TS_DJ.Infrastructure.YtDlp;

namespace TS_DJ.App.Services;

/// <summary>
/// Enriches YouTube playlist queue entries in the background without blocking playback.
/// </summary>
public sealed class YoutubePlaylistEnrichmentService
{
    private static readonly TimeSpan DelayBetweenItems = TimeSpan.FromMilliseconds(250);

    private readonly YoutubeMediaSource _youtubeMediaSource;
    private readonly IAudioMixerService _mixer;
    private readonly ILogger<YoutubePlaylistEnrichmentService> _logger;

    public YoutubePlaylistEnrichmentService(
        YoutubeMediaSource youtubeMediaSource,
        IAudioMixerService mixer,
        ILogger<YoutubePlaylistEnrichmentService> logger)
    {
        _youtubeMediaSource = youtubeMediaSource;
        _mixer = mixer;
        _logger = logger;
    }

    public void Start(IReadOnlyList<PlaybackQueueItem> importedItems)
    {
        if (importedItems.Count == 0)
            return;

        _ = Task.Run(() => EnrichAsync(importedItems, CancellationToken.None));
    }

    private async Task EnrichAsync(IReadOnlyList<PlaybackQueueItem> importedItems, CancellationToken cancellationToken)
    {
        var enriched = 0;
        foreach (var template in importedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!NeedsEnrichment(template))
                continue;

            try
            {
                var scratch = new PlaybackQueueItem
                {
                    SourceKind = PlaybackSourceKind.YouTube,
                    VideoUrl = template.VideoUrl,
                    DisplayName = template.DisplayName,
                    Artist = template.Artist,
                    DurationSeconds = template.DurationSeconds,
                    Status = PlaybackQueueStatus.Queued
                };

                await _youtubeMediaSource.EnrichQueueItemMetadataAsync(scratch, cancellationToken);

                _mixer.UpdateQueueItemMetadata(
                    template.SourceKey,
                    scratch.DisplayName,
                    scratch.Artist,
                    scratch.DurationSeconds);

                enriched++;
            }
            catch (YtDlpException ex)
            {
                _logger.LogDebug(ex, "Background metadata enrichment skipped for {SourceKey}", template.SourceKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background metadata enrichment failed for {SourceKey}", template.SourceKey);
            }

            try
            {
                await Task.Delay(DelayBetweenItems, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (enriched > 0)
        {
            _logger.LogInformation(
                "Background YouTube playlist enrichment updated {Count} queue item(s)",
                enriched);
        }
    }

    private static bool NeedsEnrichment(PlaybackQueueItem item) =>
        string.IsNullOrWhiteSpace(item.DisplayName)
        || item.DisplayName.Equals("Unknown Title", StringComparison.OrdinalIgnoreCase)
        || item.DurationSeconds is null or <= 0;
}
