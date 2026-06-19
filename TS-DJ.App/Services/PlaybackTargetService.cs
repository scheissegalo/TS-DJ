using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.Services;

public interface IPlaybackTargetService
{
    DeckId SelectedLoadDeck { get; set; }

    bool IsDualDeckMode { get; }

    Task<int> LoadItemAsync(PlaybackQueueItem item, bool playImmediately, CancellationToken cancellationToken = default);

    Task<int> LoadItemsAsync(
        IReadOnlyList<PlaybackQueueItem> items,
        bool playImmediately,
        bool replaceQueue,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Routes media loads to the queue (sequential mode) or a deck (dual-deck mode).
/// </summary>
public sealed class PlaybackTargetService : IPlaybackTargetService
{
    private readonly IAudioMixerService _mixer;
    private readonly IAudioPlaybackService _playback;

    public PlaybackTargetService(IAudioMixerService mixer, IAudioPlaybackService playback)
    {
        _mixer = mixer;
        _playback = playback;
    }

    public DeckId SelectedLoadDeck { get; set; } = DeckId.A;

    public bool IsDualDeckMode =>
        _mixer.PlaybackSettings.Mode == PlaybackMode.DualDeck;

    public async Task<int> LoadItemAsync(
        PlaybackQueueItem item,
        bool playImmediately,
        CancellationToken cancellationToken = default)
    {
        if (IsDualDeckMode)
        {
            await _playback.LoadToDeckAsync(SelectedLoadDeck, item, cancellationToken);
            if (playImmediately)
                await _playback.PlayDeckAsync(SelectedLoadDeck, cancellationToken);
            return 0;
        }

        _mixer.Enqueue(item);

        if (!playImmediately)
            return -1;

        var index = FindQueueIndex(item.SourceKey);
        if (index >= 0)
            await _playback.PlayQueueItemAsync(index, cancellationToken);

        return index;
    }

    public async Task<int> LoadItemsAsync(
        IReadOnlyList<PlaybackQueueItem> items,
        bool playImmediately,
        bool replaceQueue,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
            return -1;

        if (IsDualDeckMode)
        {
            await _playback.LoadToDeckAsync(SelectedLoadDeck, items[0], cancellationToken);
            if (playImmediately)
                await _playback.PlayDeckAsync(SelectedLoadDeck, cancellationToken);
            return 0;
        }

        if (replaceQueue)
            _mixer.ClearQueue();

        _mixer.EnqueueRange(items);

        if (!playImmediately)
            return -1;

        var index = FindQueueIndex(items[0].SourceKey);
        if (index >= 0)
            await _playback.PlayQueueItemAsync(index, cancellationToken);

        return index;
    }

    private int FindQueueIndex(string sourceKey) =>
        _mixer.Queue
            .Select((queueItem, idx) => (queueItem, idx))
            .FirstOrDefault(pair => pair.queueItem.SourceKey == sourceKey)
            .idx;
}
