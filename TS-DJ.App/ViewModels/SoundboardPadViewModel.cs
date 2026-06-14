using CommunityToolkit.Mvvm.ComponentModel;

namespace TS_DJ.App.ViewModels;

public partial class SoundboardPadViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string? _hotkey;

    [ObservableProperty]
    private int _gainHuman = 100;

    [ObservableProperty]
    private bool _isTriggered;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _highlightBorderThickness = 1;

    public string FileNameDisplay =>
        string.IsNullOrWhiteSpace(FilePath) ? "—" : System.IO.Path.GetFileName(FilePath);

    public string HotkeyDisplay =>
        string.IsNullOrWhiteSpace(Hotkey) ? "—" : Hotkey;

    public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

    public bool CanPlay => IsConnected && HasFile;

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(FileNameDisplay));
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(CanPlay));
    }

    partial void OnHotkeyChanged(string? value) => OnPropertyChanged(nameof(HotkeyDisplay));

    partial void OnIsTriggeredChanged(bool value) => HighlightBorderThickness = value ? 2 : 1;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(CanPlay));
}
