using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.TeamSpeak;

namespace TS_DJ.App.ViewModels;

public partial class SoundboardPadSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<SoundboardPadSettingsViewModel> _logger;
    private readonly ISoundboardService _soundboardService;
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly int _padIndex;
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isLoading;
    private bool _disposed;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string? _hotkey;

    [ObservableProperty]
    private double _gainHuman = 100;

    [ObservableProperty]
    private bool _isCapturingHotkey;

    [ObservableProperty]
    private bool _isConnected;

    public string PadTitle => $"Pad {_padIndex + 1} Settings";

    public string FileNameDisplay =>
        string.IsNullOrWhiteSpace(FilePath) ? "No file assigned" : System.IO.Path.GetFileName(FilePath);

    public string HotkeyDisplay =>
        string.IsNullOrWhiteSpace(Hotkey) ? "—" : Hotkey;

    public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

    public bool CanPlay => IsConnected && HasFile;

    public SoundboardPadSettingsViewModel(
        ILogger<SoundboardPadSettingsViewModel> logger,
        ISoundboardService soundboardService,
        ITeamSpeakService teamSpeakService,
        int padIndex)
    {
        _logger = logger;
        _soundboardService = soundboardService;
        _teamSpeakService = teamSpeakService;
        _padIndex = padIndex;

        _soundboardService.PadsChanged += OnPadsChanged;
        _teamSpeakService.StateChanged += OnConnectionStateChanged;

        SyncFromService();
    }

    public bool TryHandleHotkey(Key key, KeyModifiers modifiers)
    {
        if (!IsCapturingHotkey)
            return false;

        if (key is Key.Escape)
        {
            IsCapturingHotkey = false;
            return true;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return true;

        var hotkey = HotkeyFormatter.Format(key, modifiers);
        _soundboardService.SetPadHotkey(_padIndex, hotkey);
        ScheduleSave();
        IsCapturingHotkey = false;
        return true;
    }

    [RelayCommand]
    private void StartHotkeyCapture() => IsCapturingHotkey = true;

    [RelayCommand]
    private void ClearHotkey()
    {
        _soundboardService.SetPadHotkey(_padIndex, null);
        ScheduleSave();
    }

    [RelayCommand]
    private async Task AssignFileAsync()
    {
        var path = await PickAudioFileAsync();
        if (path is null)
            return;

        try
        {
            _soundboardService.AssignPad(_padIndex, path, Label);
            ScheduleSave();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign pad {Index}", _padIndex);
        }
    }

    [RelayCommand]
    private void ClearPad()
    {
        _soundboardService.ClearPad(_padIndex);
        ScheduleSave();
    }

    [RelayCommand]
    private void Preview()
    {
        if (!CanPlay)
            return;

        _soundboardService.PlayPad(_padIndex);
    }

    partial void OnLabelChanged(string value)
    {
        if (_isLoading || string.IsNullOrWhiteSpace(value))
            return;

        _soundboardService.SetPadLabel(_padIndex, value.Trim());
        ScheduleSave();
    }

    partial void OnGainHumanChanged(double value)
    {
        if (_isLoading)
            return;

        _soundboardService.SetPadGain(_padIndex, (int)Math.Round(value));
        ScheduleSave();
    }

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(FileNameDisplay));
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(CanPlay));
    }

    partial void OnHotkeyChanged(string? value) => OnPropertyChanged(nameof(HotkeyDisplay));

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(CanPlay));

    private void OnPadsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(SyncFromService);

    private void SyncFromService()
    {
        var pad = _soundboardService.Pads.FirstOrDefault(p => p.Index == _padIndex);
        if (pad is null)
            return;

        _isLoading = true;
        try
        {
            Label = pad.Label;
            FilePath = pad.FilePath;
            Hotkey = pad.Hotkey;
            GainHuman = pad.GainHuman;
            IsConnected = _teamSpeakService.State == ConnectionState.Connected;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = state == ConnectionState.Connected;
        });
    }

    private static async Task<string?> PickAudioFileAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        if (desktop.MainWindow is not Window window)
            return null;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select soundboard audio",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio") { Patterns = ["*.mp3", "*.wav", "*.aiff", "*.aif", "*.flac"] }
            ]
        });

        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    private void ScheduleSave()
    {
        if (_isLoading)
            return;

        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
        _saveDebounceCts = new CancellationTokenSource();
        _ = DebouncedSaveAsync(_saveDebounceCts.Token);
    }

    private async Task DebouncedSaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await _soundboardService.SaveAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save soundboard pad settings");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _soundboardService.PadsChanged -= OnPadsChanged;
        _teamSpeakService.StateChanged -= OnConnectionStateChanged;
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
    }
}
