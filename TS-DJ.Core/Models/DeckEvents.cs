using TS_DJ.Core.Models;

namespace TS_DJ.Core.Models;

public sealed class DeckStateChangedEventArgs : EventArgs
{
    public DeckStateChangedEventArgs(DeckId deckId, PlaybackQueueItem? loadedItem, bool isPlaying)
    {
        DeckId = deckId;
        LoadedItem = loadedItem;
        IsPlaying = isPlaying;
    }

    public DeckId DeckId { get; }
    public PlaybackQueueItem? LoadedItem { get; }
    public bool IsPlaying { get; }
}

public sealed class PlaybackModeChangedEventArgs : EventArgs
{
    public PlaybackModeChangedEventArgs(PlaybackMode mode) => Mode = mode;

    public PlaybackMode Mode { get; }
}
