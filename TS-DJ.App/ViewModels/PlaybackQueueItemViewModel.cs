using CommunityToolkit.Mvvm.ComponentModel;
using TS_DJ.Core.Models;

namespace TS_DJ.App.ViewModels;

public sealed partial class PlaybackQueueItemViewModel : ObservableObject
{
    public PlaybackQueueItemViewModel(PlaybackQueueItem item)
    {
        FilePath = item.FilePath;
        DisplayName = item.DisplayName;
        Status = item.Status;
    }

    public string FilePath { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private PlaybackQueueStatus _status;

    public string StatusText => Status.ToString();

    public string DisplayText => $"{DisplayName} ({StatusText})";

    public void Update(PlaybackQueueItem item)
    {
        Status = item.Status;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayText));
    }
}
