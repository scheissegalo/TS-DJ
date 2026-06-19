using Microsoft.Extensions.Logging;
using TS_DJ.App.Services;
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
/// Enqueues YouTube URLs through the playback target (queue or deck).
/// </summary>
public sealed class YoutubeMediaQueueService : IYoutubeMediaQueueService
{
    private readonly YoutubeMediaSource _youtubeMediaSource;
    private readonly YoutubePlaylistEnrichmentService _enrichmentService;
    private readonly IPlaybackTargetService _playbackTarget;
    private readonly ILogger<YoutubeMediaQueueService> _logger;

    public YoutubeMediaQueueService(
        YoutubeMediaSource youtubeMediaSource,
        YoutubePlaylistEnrichmentService enrichmentService,
        IPlaybackTargetService playbackTarget,
        ILogger<YoutubeMediaQueueService> logger)
    {
        _youtubeMediaSource = youtubeMediaSource;
        _enrichmentService = enrichmentService;
        _playbackTarget = playbackTarget;
        _logger = logger;
    }

    public async Task<int> EnqueueUrlAsync(string url, bool playImmediately, CancellationToken cancellationToken = default)
    {
        var item = await _youtubeMediaSource.TryCreateFromInputAsync(url, cancellationToken);
        if (item is null)
            throw new InvalidOperationException("Invalid YouTube URL.");

        return await _playbackTarget.LoadItemAsync(item, playImmediately, cancellationToken);
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

        if (!_playbackTarget.IsDualDeckMode)
            _enrichmentService.Start(items);

        var index = await _playbackTarget.LoadItemsAsync(items, playImmediately, replaceQueue, cancellationToken);

        _logger.LogInformation(
            "YouTube playlist '{Title}' ({Count} videos): dualDeck={DualDeck}, playImmediately={Play}, index={Index}",
            playlist.Title,
            items.Count,
            _playbackTarget.IsDualDeckMode,
            playImmediately,
            index);

        return index;
    }
}
