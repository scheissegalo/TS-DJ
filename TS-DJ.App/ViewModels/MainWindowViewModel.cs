using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly IAudioMixerService _audioMixerService;
    private readonly ISettingsService _settingsService;
    private readonly ILogService _logService;
    private bool _isLoadingSettings;

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
    private string _nowPlayingDisplay = "Nothing playing";

    [ObservableProperty]
    private string _playbackStatus = "Stopped";

    [ObservableProperty]
    private double _volume = 50;

    [ObservableProperty]
    private double _masterVolume = 50;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _hasQueueItems;

    [ObservableProperty]
    private OpusBitrateOption? _selectedOpusBitrate;

    [ObservableProperty]
    private string _opusBitrateDisplay = OpusBitratePresets.Label(OpusBitratePresets.Default);

    public bool CanConnect => !IsConnected && !IsConnecting;
    public bool CanDisconnect => IsConnected;
    public bool CanPlay => IsConnected && HasQueueItems && !IsPlaying && !IsPaused;
    public bool CanPause => IsPlaying;
    public bool CanStop => IsPlaying || IsPaused;
    public bool CanBrowse => IsConnected;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<PlaybackQueueItemViewModel> QueueItems { get; } = [];
    public ObservableCollection<OpusBitrateOption> OpusBitrateOptions { get; } =
    [
        new(OpusBitratePresets.Low, OpusBitratePresets.Label(OpusBitratePresets.Low)),
        new(OpusBitratePresets.Medium, OpusBitratePresets.Label(OpusBitratePresets.Medium)),
        new(OpusBitratePresets.High, OpusBitratePresets.Label(OpusBitratePresets.High)),
        new(OpusBitratePresets.VeryHigh, OpusBitratePresets.Label(OpusBitratePresets.VeryHigh))
    ];

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        ITeamSpeakService teamSpeakService,
        IAudioPlaybackService audioPlaybackService,
        IAudioMixerService audioMixerService,
        ISettingsService settingsService,
        ILogService logService)
    {
        _logger = logger;
        _teamSpeakService = teamSpeakService;
        _audioPlaybackService = audioPlaybackService;
        _audioMixerService = audioMixerService;
        _settingsService = settingsService;
        _logService = logService;

        _teamSpeakService.StateChanged += OnConnectionStateChanged;
        _teamSpeakService.StatusMessage += OnStatusMessage;
        _audioPlaybackService.StateChanged += OnPlaybackStateChanged;
        _audioMixerService.QueueChanged += OnQueueChanged;
        _audioMixerService.NowPlayingChanged += OnNowPlayingChanged;
        _audioMixerService.EncoderBitrateChanged += OnEncoderBitrateChanged;
        _logService.EntryAdded += OnLogEntryAdded;

        foreach (var entry in _logService.Entries)
            LogEntries.Add(entry);

        RefreshQueue();
        UpdateNowPlayingDisplay();

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _isLoadingSettings = true;
        try
        {
            var settings = await _settingsService.LoadConnectionSettingsAsync();
            ServerAddress = settings.Address;
            Nickname = settings.Nickname;
            ServerPassword = settings.ServerPassword;
            Channel = settings.Channel;
            Volume = AudioValues.FactorToHumanVolume(_audioPlaybackService.Volume);
            MasterVolume = AudioValues.FactorToHumanVolume(_audioMixerService.MasterVolume);

            var audioSettings = await _settingsService.LoadAudioSettingsAsync();
            _audioMixerService.EncoderBitrateKbps = audioSettings.OpusBitrateKbps;
            OpusBitrateDisplay = OpusBitratePresets.Label(_audioMixerService.EncoderBitrateKbps);
            SelectedOpusBitrate = OpusBitrateOptions.FirstOrDefault(option => option.Kbps == _audioMixerService.EncoderBitrateKbps)
                ?? OpusBitrateOptions[1];
        }
        catch (Exception ex)
        {
            LogError("Failed to load saved settings", ex);
        }
        finally
        {
            _isLoadingSettings = false;
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
                new FilePickerFileType("Audio") { Patterns = ["*.mp3", "*.wav", "*.aiff", "*.aif"] }
            ]
        });

        if (files.Count == 0)
            return;

        var path = files[0].Path.LocalPath;
        try
        {
            await _audioPlaybackService.LoadAsync(path);
            SelectedFile = path;
            _logger.LogInformation(
                "Browse enqueued {Path} (mixer queue size={Size})",
                path, _audioMixerService.Queue.Count);
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

    partial void OnMasterVolumeChanged(double value)
    {
        _audioMixerService.MasterVolume = AudioValues.HumanVolumeToFactor((float)value);
    }

    partial void OnSelectedOpusBitrateChanged(OpusBitrateOption? value)
    {
        if (_isLoadingSettings || value is null)
            return;

        _audioMixerService.EncoderBitrateKbps = value.Kbps;
        OpusBitrateDisplay = OpusBitratePresets.Label(_audioMixerService.EncoderBitrateKbps);
        _ = SaveAudioSettingsAsync();
    }

    private async Task SaveAudioSettingsAsync()
    {
        try
        {
            await _settingsService.SaveAudioSettingsAsync(new AudioSettings
            {
                OpusBitrateKbps = _audioMixerService.EncoderBitrateKbps
            });
        }
        catch (Exception ex)
        {
            LogError("Failed to save audio settings", ex);
        }
    }

    partial void OnIsConnectedChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnIsConnectingChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnIsPlayingChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnIsPausedChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnHasQueueItemsChanged(bool value) => NotifyCommandStatesChanged();

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
        _logger.LogInformation("UI: PlaybackStateChanged → {State}", state);
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = state == PlaybackState.Playing;
            IsPaused = state == PlaybackState.Paused;
            PlaybackStatus = state.ToString();
            _logger.LogDebug("UI dispatcher: IsPlaying={IsPlaying}, PlaybackStatus={Status}", IsPlaying, PlaybackStatus);
        });
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        _logger.LogInformation(
            "UI: QueueChanged received (mixer total={Total})",
            _audioMixerService.Queue.Count);
        Dispatcher.UIThread.Post(SyncPlaybackUiFromMixer);
    }

    private void OnNowPlayingChanged(object? sender, EventArgs e)
    {
        _logger.LogInformation(
            "UI: NowPlayingChanged received → {NowPlaying}",
            _audioMixerService.NowPlaying?.DisplayName ?? "none");
        Dispatcher.UIThread.Post(SyncPlaybackUiFromMixer);
    }

    /// <summary>
    /// Single UI sync point — reads queue and now-playing directly from the mixer (source of truth).
    /// </summary>
    private void SyncPlaybackUiFromMixer()
    {
        var queue = _audioMixerService.Queue;
        var queued = queue.Count(i => i.Status == PlaybackQueueStatus.Queued);
        var playing = queue.Count(i => i.Status == PlaybackQueueStatus.Playing);
        var played = queue.Count(i => i.Status == PlaybackQueueStatus.Played);

        _logger.LogInformation(
            "UI dispatcher sync: total={Total}, queued={Queued}, playing={Playing}, played={Played}, nowPlaying={NowPlaying}",
            queue.Count, queued, playing, played,
            _audioMixerService.NowPlaying?.DisplayName ?? "none");

        RefreshQueue();
        UpdateNowPlayingDisplay();
    }

    private void RefreshQueue()
    {
        var queue = _audioMixerService.Queue;
        var hadQueueItems = HasQueueItems;
        HasQueueItems = queue.Any(item =>
            item.Status is PlaybackQueueStatus.Queued or PlaybackQueueStatus.Playing);

        if (hadQueueItems != HasQueueItems)
        {
            _logger.LogInformation(
                "UI: HasQueueItems {Old} → {New} (queue count={Count})",
                hadQueueItems, HasQueueItems, queue.Count);
        }

        for (var i = 0; i < queue.Count; i++)
        {
            if (i < QueueItems.Count)
            {
                QueueItems[i].Update(queue[i]);
            }
            else
            {
                QueueItems.Add(new PlaybackQueueItemViewModel(queue[i]));
                _logger.LogDebug("UI: added queue row {Index}: {Name}", i, queue[i].DisplayName);
            }
        }

        while (QueueItems.Count > queue.Count)
        {
            _logger.LogDebug("UI: removed stale queue row at index {Index}", QueueItems.Count - 1);
            QueueItems.RemoveAt(QueueItems.Count - 1);
        }
    }

    private void UpdateNowPlayingDisplay()
    {
        var display = _audioMixerService.NowPlaying?.DisplayName ?? "Nothing playing";
        if (NowPlayingDisplay != display)
        {
            _logger.LogInformation("UI: NowPlayingDisplay → {Display}", display);
            NowPlayingDisplay = display;
        }
    }

    private void OnEncoderBitrateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
            OpusBitrateDisplay = OpusBitratePresets.Label(_audioMixerService.EncoderBitrateKbps));
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
