using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using TS_DJ.App.Services;
using TS_DJ.App.ViewModels;

namespace TS_DJ.App.Views;

public partial class MainWindow : Window
{
    private readonly ApplicationShutdownService _shutdownService;
    private bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();
        _shutdownService = App.Services.GetRequiredService<ApplicationShutdownService>();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await RestoreWindowSettingsAsync();
        Focus();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isShuttingDown)
            return;

        e.Cancel = true;
        _isShuttingDown = true;

        await SaveWindowSettingsAsync();
        await _shutdownService.ShutdownAsync();
        Close();
    }

    private async Task RestoreWindowSettingsAsync()
    {
        var settings = await _shutdownService.LoadWindowSettingsAsync();

        if (settings.Width is > 720)
            Width = settings.Width.Value;
        if (settings.Height is > 520)
            Height = settings.Height.Value;

        if (settings.X is not null && settings.Y is not null)
            Position = new PixelPoint((int)settings.X.Value, (int)settings.Y.Value);

        WindowState = settings.WindowState switch
        {
            "Maximized" => WindowState.Maximized,
            "Minimized" => WindowState.Minimized,
            _ => WindowState.Normal
        };
    }

    private async Task SaveWindowSettingsAsync()
    {
        var state = WindowState switch
        {
            WindowState.Maximized => "Maximized",
            WindowState.Minimized => "Minimized",
            _ => "Normal"
        };

        await _shutdownService.SaveWindowSettingsAsync(
            Width,
            Height,
            Position.X,
            Position.Y,
            state,
            DataContext is MainWindowViewModel vm && vm.IsSoundboardVisible);
    }

    private void QueueListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.PlaySelectedQueueItemCommand.CanExecute(null))
            viewModel.PlaySelectedQueueItemCommand.Execute(null);
    }

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel mainVm)
            return;

        if (mainVm.Soundboard.TryHandleHotkey(e.Key, e.KeyModifiers))
            e.Handled = true;
    }
}
