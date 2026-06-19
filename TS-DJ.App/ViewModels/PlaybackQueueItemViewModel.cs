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

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private PlaybackQueueStatus _status;

    public bool IsCurrentlyPlaying => Status == PlaybackQueueStatus.Playing;

    public string StatusText => Status.ToString();

    public string DisplayText => $"{DisplayName} ({StatusText})";

    public void UpdateFrom(PlaybackQueueItem item)
    {
        if (DisplayName != item.DisplayName)
            DisplayName = item.DisplayName;

        if (Status != item.Status)
            Status = item.Status;
    }

    partial void OnStatusChanged(PlaybackQueueStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(IsCurrentlyPlaying));
    }

    partial void OnDisplayNameChanged(string value) =>
        OnPropertyChanged(nameof(DisplayText));
}
