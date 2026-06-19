using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Core.Services;

public interface IAudioMixerService : IDisposable
{
    float MasterVolume { get; set; }

    IDeckChannel DeckA { get; }
    IDeckChannel DeckB { get; }
    DeckId ActiveDeckId { get; }
    IDeckChannel ActiveDeck { get; }

    /// <summary>Backward-compatible alias for <see cref="ActiveDeck"/>.</summary>
    IMusicTrackSource Music { get; }

    ISoundEffectSource Soundboard { get; }
    IMicrophoneSource Microphone { get; }

    PlaybackSettings PlaybackSettings { get; }
    bool CrossfadeInProgress { get; }

    IReadOnlyList<PlaybackQueueItem> Queue { get; }
    PlaybackQueueItem? NowPlaying { get; }

    event EventHandler? QueueChanged;
    event EventHandler? NowPlayingChanged;
    event EventHandler? AdvancePendingChanged;
    event EventHandler<DeckStateChangedEventArgs>? DeckStateChanged;
    event EventHandler<PlaybackModeChangedEventArgs>? PlaybackModeChanged;

    bool AdvancePending { get; }

    void SetPlaybackSettings(PlaybackSettings settings);

    void Enqueue(string filePath);
    void Enqueue(PlaybackQueueItem item);
    void EnqueueRange(IEnumerable<PlaybackQueueItem> items);
    void RemoveFromQueue(int index);
    void ClearQueue();
    void UpdateQueueItemMetadata(string sourceKey, string? displayName = null, string? artist = null, int? durationSeconds = null);
    void PlayQueueItem(int index);
    Task PlayQueueItemAsync(int index, CancellationToken cancellationToken = default);
    Task LoadToDeckAsync(DeckId deckId, PlaybackQueueItem item, CancellationToken cancellationToken = default);
    Task PlayDeckAsync(DeckId deckId, CancellationToken cancellationToken = default);
    void StopDeck(DeckId deckId);
    void CueDeck(DeckId deckId);
    void SkipNext();
    void SkipPrevious();
    bool CanSkipPrevious { get; }

    void Start();
    void Pause();
    void Stop();

    void PlaySoundEffect(string filePath, float gainFactor = 1f);

    float SoundboardVolume { get; set; }

    int EncoderBitrateKbps { get; set; }

    event EventHandler? EncoderBitrateChanged;
}
