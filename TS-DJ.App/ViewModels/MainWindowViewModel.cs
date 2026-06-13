using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS_DJ.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly ISettingsService _settingsService;
    private readonly ILogService _logService;

    [ObservableProperty]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    private string _nickname = "TS-DJ";

    [ObservableProperty]
    private string _serverPassword = string.Empty;

    [ObservableProperty]
    private string _channel = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = "● Disconnected";

    [ObservableProperty]
    private string _selectedFile = "No file selected";

    [ObservableProperty]
    private string _playbackStatus = "Stopped";

    [ObservableProperty]
    private double _volume = 50;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _hasFileLoaded;

    public bool CanConnect => !IsConnected && !IsConnecting;
    public bool CanDisconnect => IsConnected;
    public bool CanPlay => IsConnected && HasFileLoaded && !IsPlaying;
    public bool CanPause => IsPlaying;
    public bool CanStop => IsPlaying || IsPaused;
    public bool CanBrowse => !IsPlaying;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public MainWindowViewModel(
        ITeamSpeakService teamSpeakService,
        IAudioPlaybackService audioPlaybackService,
        ISettingsService settingsService,
        ILogService logService)
    {
        _teamSpeakService = teamSpeakService;
        _audioPlaybackService = audioPlaybackService;
        _settingsService = settingsService;
        _logService = logService;

        _teamSpeakService.StateChanged += OnConnectionStateChanged;
        _teamSpeakService.StatusMessage += OnStatusMessage;
        _audioPlaybackService.StateChanged += OnPlaybackStateChanged;
        _logService.EntryAdded += OnLogEntryAdded;

        foreach (var entry in _logService.Entries)
            LogEntries.Add(entry);

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsService.LoadConnectionSettingsAsync();
            ServerAddress = settings.Address;
            Nickname = settings.Nickname;
            ServerPassword = settings.ServerPassword;
            Channel = settings.Channel;
            Volume = AudioValues.FactorToHumanVolume(_audioPlaybackService.Volume);
        }
        catch (Exception ex)
        {
            LogError("Failed to load saved settings", ex);
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var settings = new ConnectionSettings
        {
            Address = ServerAddress,
            Nickname = Nickname,
            ServerPassword = ServerPassword,
            Channel = Channel
        };

        try
        {
            await _settingsService.SaveConnectionSettingsAsync(settings);
            await _teamSpeakService.ConnectAsync(settings);
        }
        catch (Exception ex)
        {
            LogError("Connect failed while saving settings or starting connection", ex);
            ConnectionStatus = "● Error — see log";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            await _teamSpeakService.DisconnectAsync();
        }
        catch (Exception ex)
        {
            LogError("Disconnect failed", ex);
        }
    }

    [RelayCommand]
    private async Task BrowseFileAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (desktop.MainWindow is not Window window)
            return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select audio file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio") { Patterns = ["*.mp3", "*.wav", "*.flac", "*.ogg", "*.m4a"] }
            ]
        });

        if (files.Count == 0)
            return;

        var path = files[0].Path.LocalPath;
        try
        {
            await _audioPlaybackService.LoadAsync(path);
            SelectedFile = path;
            HasFileLoaded = true;
            NotifyCommandStatesChanged();
        }
        catch (Exception ex)
        {
            LogError($"Failed to load audio file '{path}'", ex);
        }
    }

    [RelayCommand]
    private async Task PlayAsync() => await _audioPlaybackService.PlayAsync();

    [RelayCommand]
    private async Task PauseAsync() => await _audioPlaybackService.PauseAsync();

    [RelayCommand]
    private async Task StopAsync() => await _audioPlaybackService.StopAsync();

    partial void OnVolumeChanged(double value)
    {
        _audioPlaybackService.Volume = AudioValues.HumanVolumeToFactor((float)value);
    }

    partial void OnIsConnectedChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnIsConnectingChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnIsPlayingChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnIsPausedChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnHasFileLoadedChanged(bool value) => NotifyCommandStatesChanged();

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnecting = state == ConnectionState.Connecting;
            IsConnected = state == ConnectionState.Connected;
            ConnectionStatus = state switch
            {
                ConnectionState.Connected => "● Connected",
                ConnectionState.Connecting => "● Connecting…",
                ConnectionState.Error => "● Error",
                _ => "● Disconnected"
            };
        });
    }

    private void OnStatusMessage(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ConnectionStatus = IsConnected
                ? $"● Connected — {message}"
                : message.StartsWith("●", StringComparison.Ordinal)
                    ? message
                    : $"● {message}";
        });
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = state == PlaybackState.Playing;
            IsPaused = state == PlaybackState.Paused;
            PlaybackStatus = state.ToString();
        });
    }

    private void OnLogEntryAdded(object? sender, LogEntry entry)
    {
        Dispatcher.UIThread.Post(() => LogEntries.Add(entry));
    }

    private void NotifyCommandStatesChanged()
    {
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanBrowse));
    }

    private void LogError(string message, Exception ex)
    {
        var detail = ex.InnerException is null
            ? ex.Message
            : $"{ex.Message} ({ex.InnerException.Message})";

        _logService.Add(new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = "Error",
            Message = $"{message}: {detail}"
        });
    }
}
