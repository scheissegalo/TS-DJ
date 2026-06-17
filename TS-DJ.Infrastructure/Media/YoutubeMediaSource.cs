using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.YtDlp;

namespace TS_DJ.Infrastructure.Media;

public sealed class YoutubeMediaSource : IMediaSource, IPlaybackStreamOpener
{
    private readonly YtDlpService _ytDlpService;

    public YoutubeMediaSource(YtDlpService ytDlpService)
    {
        _ytDlpService = ytDlpService;
    }

    public PlaybackSourceKind SourceKind => PlaybackSourceKind.YouTube;

    public bool CanHandleInput(string input) => YoutubeUrlHelper.IsYouTubeUrl(input);

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
        if (!YoutubeUrlHelper.TryNormalize(input, out var normalizedUrl))
            return null;

        var metadata = await _ytDlpService.FetchMetadataAsync(normalizedUrl, cancellationToken);

        return new PlaybackQueueItem
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

        return await _ytDlpService.OpenMp3StreamAsync(normalizedUrl, cancellationToken);
    }
}
