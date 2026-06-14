using CommunityToolkit.Mvvm.ComponentModel;
using TS_DJ.Core.Models;

namespace TS_DJ.App.ViewModels;

public sealed partial class PlaybackQueueItemViewModel : ObservableObject
{
    public PlaybackQueueItemViewModel(PlaybackQueueItem item)
    {
        SourceKey = item.SourceKey;
        FilePath = item.FilePath;
        DisplayName = item.DisplayName;
        Status = item.Status;
    }

    public string SourceKey { get; }
    public string FilePath { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private PlaybackQueueStatus _status;

    public bool IsCurrentlyPlaying => Status == PlaybackQueueStatus.Playing;

    public string StatusText => Status.ToString();

    public string DisplayText => $"{DisplayName} ({StatusText})";

    partial void OnStatusChanged(PlaybackQueueStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(IsCurrentlyPlaying));
    }
}
