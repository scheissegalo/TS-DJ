using CommunityToolkit.Mvvm.ComponentModel;
using TS_DJ.Core.Models;

namespace TS_DJ.App.ViewModels;

public partial class DeckViewModel : ViewModelBase
{
    public DeckViewModel(DeckId deckId)
    {
        DeckId = deckId;
        Title = deckId == DeckId.A ? "Deck A" : "Deck B";
    }

    public DeckId DeckId { get; }
    public string Title { get; }

    [ObservableProperty]
    private string _trackDisplay = "Empty";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _hasLoadedTrack;

    [ObservableProperty]
    private double _volume = 100;

    public void UpdateFrom(PlaybackQueueItem? item, bool isPlaying)
    {
        HasLoadedTrack = item is not null;
        IsPlaying = isPlaying;
        TrackDisplay = item is null
            ? "Empty"
            : string.IsNullOrWhiteSpace(item.Artist)
                ? item.DisplayName
                : $"{item.Artist} — {item.DisplayName}";
    }
}
