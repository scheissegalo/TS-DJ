using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TS_DJ.App.ViewModels;

namespace TS_DJ.App.Views;

public partial class SoundboardPadSettingsWindow : Window
{
    public SoundboardPadSettingsWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SoundboardPadSettingsViewModel vm)
            return;

        if (vm.TryHandleHotkey(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }
}
