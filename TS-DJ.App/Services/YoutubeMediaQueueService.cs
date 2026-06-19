using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.Media;
using TS_DJ.Infrastructure.YtDlp;

namespace TS_DJ.App.Services;

public interface IYoutubeMediaQueueService
{
    Task<int> EnqueueUrlAsync(string url, bool playImmediately, CancellationToken cancellationToken = default);

    Task<YoutubePlaylistMetadata> FetchPlaylistPreviewAsync(string url, CancellationToken cancellationToken = default);

    Task<int> EnqueuePlaylistAsync(
        string url,
        bool playImmediately,
        bool replaceQueue,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Enqueues YouTube URLs through the shared playback queue.
/// </summary>
public sealed class YoutubeMediaQueueService : IYoutubeMediaQueueService
{
    private readonly YoutubeMediaSource _youtubeMediaSource;
    private readonly YoutubePlaylistEnrichmentService _enrichmentService;
    private readonly IAudioMixerService _mixer;
    private readonly IAudioPlaybackService _playback;
    private readonly ILogger<YoutubeMediaQueueService> _logger;

    public YoutubeMediaQueueService(
        YoutubeMediaSource youtubeMediaSource,
        YoutubePlaylistEnrichmentService enrichmentService,
        IAudioMixerService mixer,
        IAudioPlaybackService playback,
        ILogger<YoutubeMediaQueueService> logger)
    {
        _youtubeMediaSource = youtubeMediaSource;
        _enrichmentService = enrichmentService;
        _mixer = mixer;
        _playback = playback;
        _logger = logger;
    }

    public async Task<int> EnqueueUrlAsync(string url, bool playImmediately, CancellationToken cancellationToken = default)
    {
        var item = await _youtubeMediaSource.TryCreateFromInputAsync(url, cancellationToken);
        if (item is null)
            throw new InvalidOperationException("Invalid YouTube URL.");

        _mixer.EnqueueRange([item]);

        if (!playImmediately)
            return -1;

        return await PlayFirstMatchingAsync(item.SourceKey, cancellationToken);
    }

    public Task<YoutubePlaylistMetadata> FetchPlaylistPreviewAsync(
        string url,
        CancellationToken cancellationToken = default) =>
        _youtubeMediaSource.FetchPlaylistMetadataAsync(url, cancellationToken);

    public async Task<int> EnqueuePlaylistAsync(
        string url,
        bool playImmediately,
        bool replaceQueue,
        CancellationToken cancellationToken = default)
    {
        var playlist = await _youtubeMediaSource.FetchPlaylistMetadataAsync(url, cancellationToken);
        var items = _youtubeMediaSource.CreateQueueItemsFromPlaylist(playlist);

        if (replaceQueue)
            _mixer.ClearQueue();

        _mixer.EnqueueRange(items);
        _logger.LogInformation(
            "Enqueued YouTube playlist '{Title}' with {Count} videos (replaceQueue={Replace}, playImmediately={Play})",
            playlist.Title,
            items.Count,
            replaceQueue,
            playImmediately);

        _enrichmentService.Start(items);

        if (!playImmediately)
            return -1;

        if (items.Count == 0)
            return -1;

        return await PlayFirstMatchingAsync(items[0].SourceKey, cancellationToken);
    }

    private async Task<int> PlayFirstMatchingAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var index = _mixer.Queue
            .Select((item, idx) => (item, idx))
            .FirstOrDefault(pair => pair.item.SourceKey == sourceKey)
            .idx;

        if (index >= 0)
            await _playback.PlayQueueItemAsync(index, cancellationToken);

        return index;
    }
}
