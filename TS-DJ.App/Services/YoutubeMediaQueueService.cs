using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.Media;

namespace TS_DJ.App.Services;

public interface IYoutubeMediaQueueService
{
    Task<int> EnqueueUrlAsync(string url, bool playImmediately, CancellationToken cancellationToken = default);
}

/// <summary>
/// Enqueues YouTube URLs through the shared playback queue.
/// </summary>
public sealed class YoutubeMediaQueueService : IYoutubeMediaQueueService
{
    private readonly YoutubeMediaSource _youtubeMediaSource;
    private readonly IAudioMixerService _mixer;
    private readonly IAudioPlaybackService _playback;

    public YoutubeMediaQueueService(
        YoutubeMediaSource youtubeMediaSource,
        IAudioMixerService mixer,
        IAudioPlaybackService playback)
    {
        _youtubeMediaSource = youtubeMediaSource;
        _mixer = mixer;
        _playback = playback;
    }

    public async Task<int> EnqueueUrlAsync(string url, bool playImmediately, CancellationToken cancellationToken = default)
    {
        var item = await _youtubeMediaSource.TryCreateFromInputAsync(url, cancellationToken);
        if (item is null)
            throw new InvalidOperationException("Invalid YouTube URL.");

        _mixer.EnqueueRange([item]);

        if (!playImmediately)
            return -1;

        var firstKey = item.SourceKey;
        var index = _mixer.Queue
            .Select((queueItem, idx) => (queueItem, idx))
            .FirstOrDefault(pair => pair.queueItem.SourceKey == firstKey)
            .idx;

        if (index >= 0)
            await _playback.PlayQueueItemAsync(index, cancellationToken);

        return index;
    }
}
