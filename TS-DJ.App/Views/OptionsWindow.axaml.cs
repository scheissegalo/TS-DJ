using Avalonia.Controls;
using Avalonia.Interactivity;
using TS_DJ.App.ViewModels;

namespace TS_DJ.App.Views;

public partial class OptionsWindow : Window
{
    public OptionsWindow()
    {
        InitializeComponent();
    }

    private async void OptionsWindow_OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is OptionsViewModel viewModel)
            await viewModel.LoadAllAsync();
    }
}
