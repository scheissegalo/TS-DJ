using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TS_DJ.App.ViewModels;

public partial class SavePlaylistDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(CanSave));

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
            return;

        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        if (desktop.Windows.OfType<Views.SavePlaylistDialog>().FirstOrDefault() is { } window)
            window.Close(true);
    }
}
