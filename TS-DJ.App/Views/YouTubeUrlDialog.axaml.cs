using Avalonia.Controls;
using Avalonia.Interactivity;
using TS_DJ.App.ViewModels;

namespace TS_DJ.App.Views;

public partial class YouTubeUrlDialog : Window
{
    public YouTubeUrlDialog()
    {
        InitializeComponent();
    }

    public YouTubeUrlDialogViewModel ViewModel => (YouTubeUrlDialogViewModel)DataContext!;

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void AddToQueueButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (await ViewModel.AddToQueueAsync())
            Close(true);
    }

    private async void PlayNowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (await ViewModel.PlayNowAsync())
            Close(true);
    }
}
