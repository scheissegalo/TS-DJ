using Avalonia.Controls;
using Avalonia.Interactivity;
using TS_DJ.App.ViewModels;

namespace TS_DJ.App.Views;

public partial class PlaylistManagerDialog : Window
{
    public PlaylistManagerDialog()
    {
        InitializeComponent();
    }

    private async void PlaylistManagerDialog_OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is PlaylistManagerViewModel viewModel)
            await viewModel.LoadAsync();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
