using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.YtDlp;

namespace TS_DJ.Infrastructure.Media;

public sealed class YoutubeMediaSource : IMediaSource, IPlaybackStreamOpener
{
    private readonly IYoutubeService _youtubeService;

    public YoutubeMediaSource(IYoutubeService youtubeService)
    {
        _youtubeService = youtubeService;
    }

    public PlaybackSourceKind SourceKind => PlaybackSourceKind.YouTube;

    public bool CanHandleInput(string input) => YoutubeUrlHelper.IsRecognizedUrl(input);

    public bool CanHandleItem(PlaybackQueueItem item) =>
        item.SourceKind == PlaybackSourceKind.YouTube;

    public bool CanOpen(PlaybackQueueItem item) => CanHandleItem(item);

    public void ValidateForEnqueue(PlaybackQueueItem item)
    {
        if (item.SourceKind != PlaybackSourceKind.YouTube)
            throw new ArgumentException("Not a YouTube queue item.", nameof(item));

        if (string.IsNullOrWhiteSpace(item.VideoUrl))
            throw new ArgumentException("YouTube queue items require a video URL.", nameof(item));

        if (!YoutubeUrlHelper.TryNormalize(item.VideoUrl, out _))
            throw new ArgumentException("Invalid YouTube video URL.", nameof(item));
    }

    public async Task<PlaybackQueueItem?> TryCreateFromInputAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!YoutubeUrlHelper.TryClassify(input, out var classification)
            || classification.Kind != YoutubeContentKind.SingleVideo
            || string.IsNullOrWhiteSpace(classification.VideoUrl))
        {
            return null;
        }

        var metadata = await _youtubeService.FetchVideoMetadataAsync(classification.VideoUrl, cancellationToken);
        return CreateQueueItem(metadata);
    }

    public Task<YoutubePlaylistMetadata> FetchPlaylistMetadataAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!YoutubeUrlHelper.TryClassify(input, out var classification)
            || classification.Kind != YoutubeContentKind.Playlist
            || string.IsNullOrWhiteSpace(classification.PlaylistUrl))
        {
            throw new ArgumentException("Invalid YouTube playlist URL.", nameof(input));
        }

        return _youtubeService.FetchPlaylistMetadataAsync(classification.PlaylistUrl, cancellationToken);
    }

    public IReadOnlyList<PlaybackQueueItem> CreateQueueItemsFromPlaylist(YoutubePlaylistMetadata playlist) =>
        playlist.Entries.Select(entry => new PlaybackQueueItem
        {
            SourceKind = PlaybackSourceKind.YouTube,
            VideoUrl = entry.VideoUrl,
            DisplayName = entry.Title,
            Artist = entry.Uploader,
            DurationSeconds = entry.DurationSeconds,
            Status = PlaybackQueueStatus.Queued
        }).ToList();

    public Task<string?> ResolveStreamUrlAsync(PlaybackQueueItem item, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public async Task<IPlaybackStreamHandle?> OpenPlaybackStreamAsync(
        PlaybackQueueItem item,
        CancellationToken cancellationToken = default)
    {
        if (!CanHandleItem(item) || string.IsNullOrWhiteSpace(item.VideoUrl))
            return null;

        if (!YoutubeUrlHelper.TryNormalize(item.VideoUrl, out var normalizedUrl))
            return null;

        return await _youtubeService.ExtractAudioAsync(normalizedUrl, cancellationToken);
    }

    public async Task EnrichQueueItemMetadataAsync(
        PlaybackQueueItem item,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.VideoUrl)
            || !YoutubeUrlHelper.TryNormalize(item.VideoUrl, out var normalizedUrl))
        {
            return;
        }

        var metadata = await _youtubeService.FetchVideoMetadataAsync(normalizedUrl, cancellationToken);
        item.DisplayName = metadata.Title;
        item.Artist = metadata.Uploader;
        item.DurationSeconds = metadata.DurationSeconds;
        item.ThumbnailUrl = metadata.ThumbnailUrl;
    }

    private static PlaybackQueueItem CreateQueueItem(YoutubeVideoMetadata metadata) =>
        new()
        {
            SourceKind = PlaybackSourceKind.YouTube,
            VideoUrl = metadata.WebpageUrl,
            ThumbnailUrl = metadata.ThumbnailUrl,
            DisplayName = metadata.Title,
            Artist = metadata.Uploader,
            DurationSeconds = metadata.DurationSeconds,
            Status = PlaybackQueueStatus.Queued
        };
}
