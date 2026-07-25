using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TS_DJ.App.Views;

public partial class UpdateAvailableDialog : Window
{
    public UpdateAvailableDialog()
    {
        InitializeComponent();
    }

    private void LaterButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
