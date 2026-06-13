using Avalonia.Controls;
using Avalonia.Input;
using TS_DJ.App.ViewModels;

namespace TS_DJ.App.Views;

public partial class NavidromeBrowserWindow : Window
{
    public NavidromeBrowserWindow()
    {
        InitializeComponent();
    }

    private async void TracksList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is NavidromeBrowserViewModel vm && vm.SelectedTrack is not null)
            await vm.PlayTrackNowAsync(vm.SelectedTrack.Track);
    }

    private async void AlbumsList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is NavidromeBrowserViewModel vm)
            await vm.PlayActiveTabSelectionNowAsync();
    }

    private async void ArtistsList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is NavidromeBrowserViewModel vm)
            await vm.PlayActiveTabSelectionNowAsync();
    }

    private async void PlaylistsList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is NavidromeBrowserViewModel vm)
            await vm.PlayActiveTabSelectionNowAsync();
    }

    private async void PlaylistTracksList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is NavidromeBrowserViewModel vm && vm.SelectedPlaylistTrack is not null)
            await vm.PlayTrackNowAsync(vm.SelectedPlaylistTrack.Track);
    }
}
