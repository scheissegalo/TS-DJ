using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TS_DJ.App.Views;

public partial class SavePlaylistDialog : Window
{
    public SavePlaylistDialog()
    {
        InitializeComponent();
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
