using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.Services;

/// <summary>
/// Enqueues Navidrome tracks through the shared playback queue in batch.
/// </summary>
public sealed class NavidromeMediaQueueService : INavidromeMediaQueueService
{
    private readonly INavidromeService _navidromeService;
    private readonly IAudioMixerService _mixer;
    private readonly IAudioPlaybackService _playback;

    public NavidromeMediaQueueService(
        INavidromeService navidromeService,
        IAudioMixerService mixer,
        IAudioPlaybackService playback)
    {
        _navidromeService = navidromeService;
        _mixer = mixer;
        _playback = playback;
    }

    public async Task<int> EnqueueTracksAsync(IReadOnlyList<NavidromeTrack> tracks, bool playImmediately)
    {
        if (tracks.Count == 0)
            return -1;

        var items = tracks.Select(_navidromeService.CreateQueueItem).ToList();
        _mixer.EnqueueRange(items);

        if (!playImmediately)
            return -1;

        var firstKey = items[0].SourceKey;
        var index = _mixer.Queue
            .Select((item, idx) => (item, idx))
            .FirstOrDefault(pair => pair.item.SourceKey == firstKey)
            .idx;

        if (index >= 0)
            await _playback.PlayQueueItemAsync(index);

        return index;
    }
}
