using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface IAudioPlaybackService
{
    PlaybackState State { get; }
    string? CurrentFilePath { get; }
    TimeSpan CurrentPosition { get; }
    TimeSpan TotalDuration { get; }
    float Volume { get; set; }

    PlaybackSettings PlaybackSettings { get; }
    IDeckChannel DeckA { get; }
    IDeckChannel DeckB { get; }
    DeckId ActiveDeckId { get; }

    event EventHandler<PlaybackState>? StateChanged;

    Task LoadAsync(string filePath, CancellationToken cancellationToken = default);
    Task LoadAsync(PlaybackQueueItem item, CancellationToken cancellationToken = default);
    Task LoadToDeckAsync(DeckId deckId, PlaybackQueueItem item, CancellationToken cancellationToken = default);
    Task PlayDeckAsync(DeckId deckId, CancellationToken cancellationToken = default);
    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task PlayQueueItemAsync(int index, CancellationToken cancellationToken = default);
    Task SkipNextAsync(CancellationToken cancellationToken = default);
    Task SkipPreviousAsync(CancellationToken cancellationToken = default);
    void StopDeck(DeckId deckId);
    void CueDeck(DeckId deckId);
    void SetPlaybackSettings(PlaybackSettings settings);
    void RemoveFromQueue(int index);
    void ClearQueue();
}
