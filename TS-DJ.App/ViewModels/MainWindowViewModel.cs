using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.TeamSpeak;

namespace TS_DJ.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly IAudioMixerService _audioMixerService;
    private readonly ISettingsService _settingsService;
    private readonly ILogService _logService;
    private readonly TeamSpeakNicknameService _nicknameService;
    private readonly DispatcherTimer _progressTimer;
    private CancellationTokenSource? _volumeSaveDebounceCts;
    private bool _isLoadingSettings;
    private bool _disposed;

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
    private double _progressValue;

    [ObservableProperty]
    private string _elapsedDisplay = "0:00";

    [ObservableProperty]
    private string _remainingDisplay = "-0:00";

    [ObservableProperty]
    private bool _hasActiveTrack;

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

    [ObservableProperty]
    private PlaybackQueueItemViewModel? _selectedQueueItem;

    [ObservableProperty]
    private PlaybackQueueItemViewModel? _currentlyPlayingItem;

    [ObservableProperty]
    private bool _isSoundboardVisible = true;

    public string SoundboardToggleLabel => IsSoundboardVisible ? "Hide Soundboard" : "Show Soundboard";

    public bool CanConnect => !IsConnected && !IsConnecting;
    public bool CanDisconnect => IsConnected;
    public bool CanPlay => IsConnected && HasQueueItems && !IsPlaying && !IsPaused;
    public bool CanPause => IsPlaying;
    public bool CanStop => IsPlaying || IsPaused;
    public bool CanBrowse => IsConnected;
    public bool CanSkipNext => IsConnected && (IsPlaying || IsPaused || HasQueuedItemsRemaining);
    public bool CanSkipPrevious => IsConnected && (_audioMixerService.CanSkipPrevious || IsPlaying || IsPaused);
    public bool CanRemoveQueueItem =>
        SelectedQueueItem is not null && SelectedQueueItem.Status != PlaybackQueueStatus.Playing;

    public bool CanClearQueue =>
        _audioMixerService.Queue.Any(i => i.Status != PlaybackQueueStatus.Playing);

    private bool HasQueuedItemsRemaining =>
        _audioMixerService.Queue.Any(i => i.Status == PlaybackQueueStatus.Queued);

    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<PlaybackQueueItemViewModel> QueueItems { get; } = [];
    public ObservableCollection<OpusBitrateOption> OpusBitrateOptions { get; } =
    [
        new(OpusBitratePresets.Low, OpusBitratePresets.Label(OpusBitratePresets.Low)),
        new(OpusBitratePresets.Medium, OpusBitratePresets.Label(OpusBitratePresets.Medium)),
        new(OpusBitratePresets.High, OpusBitratePresets.Label(OpusBitratePresets.High)),
        new(OpusBitratePresets.VeryHigh, OpusBitratePresets.Label(OpusBitratePresets.VeryHigh))
    ];

    public SoundboardViewModel Soundboard { get; }

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        ITeamSpeakService teamSpeakService,
        IAudioPlaybackService audioPlaybackService,
        IAudioMixerService audioMixerService,
        ISettingsService settingsService,
        ILogService logService,
        TeamSpeakNicknameService nicknameService,
        SoundboardViewModel soundboardViewModel)
    {
        _logger = logger;
        _teamSpeakService = teamSpeakService;
        Soundboard = soundboardViewModel;
        _audioPlaybackService = audioPlaybackService;
        _audioMixerService = audioMixerService;
        _settingsService = settingsService;
        _logService = logService;
        _nicknameService = nicknameService;

        _teamSpeakService.StateChanged += OnConnectionStateChanged;
        _teamSpeakService.StatusMessage += OnStatusMessage;
        _audioPlaybackService.StateChanged += OnPlaybackStateChanged;
        _audioMixerService.QueueChanged += OnQueueChanged;
        _audioMixerService.NowPlayingChanged += OnNowPlayingChanged;
        _audioMixerService.EncoderBitrateChanged += OnEncoderBitrateChanged;
        _logService.EntryAdded += OnLogEntryAdded;

        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _progressTimer.Tick += (_, _) => UpdateProgressDisplay();

        foreach (var entry in _logService.Entries)
            LogEntries.Add(entry);

        RefreshQueue();
        UpdateNowPlayingDisplay();
        ResetProgressDisplay();

        _ = LoadSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        _isLoadingSettings = true;
        try
        {
            var settings = await _settingsService.LoadConnectionSettingsAsync();
            ServerAddress = settings.Address;
            Nickname = settings.Nickname;
            ServerPassword = settings.ServerPassword;
            Channel = settings.Channel;

            var audioSettings = await _settingsService.LoadAudioSettingsAsync();
            Volume = audioSettings.MusicVolumeHuman;
            MasterVolume = audioSettings.MasterVolumeHuman;
            _audioPlaybackService.Volume = AudioValues.HumanVolumeToFactor((float)Volume);
            _audioMixerService.MasterVolume = AudioValues.HumanVolumeToFactor((float)MasterVolume);

            _audioMixerService.EncoderBitrateKbps = audioSettings.OpusBitrateKbps;
            OpusBitrateDisplay = OpusBitratePresets.Label(_audioMixerService.EncoderBitrateKbps);
            SelectedOpusBitrate = OpusBitrateOptions.FirstOrDefault(option => option.Kbps == _audioMixerService.EncoderBitrateKbps)
                ?? OpusBitrateOptions[1];

            var uiSettings = await _settingsService.LoadUiSettingsAsync();
            IsSoundboardVisible = uiSettings.IsSoundboardVisible;
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

    public async Task SaveSettingsForShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _settingsService.SaveConnectionSettingsAsync(new ConnectionSettings
            {
                Address = ServerAddress,
                Nickname = Nickname,
                ServerPassword = ServerPassword,
                Channel = Channel
            }, cancellationToken);

            await SaveAudioSettingsAsync(cancellationToken);
            await Soundboard.SaveForShutdownAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogError("Failed to save settings during shutdown", ex);
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
            _nicknameService.SetBaseNickname(Nickname);
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
            await _nicknameService.RestoreBaseNicknameAsync();
            await _teamSpeakService.DisconnectAsync();
        }
        catch (Exception ex)
        {
            LogError("Disconnect failed", ex);
        }
    }

    [RelayCommand]
    private void OpenNavidromeBrowser()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var viewModel = App.Services.GetRequiredService<NavidromeBrowserViewModel>();
        var window = new Views.NavidromeBrowserWindow
        {
            DataContext = viewModel
        };

        if (desktop.MainWindow is Window owner)
            window.Show(owner);
        else
            window.Show();
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
                new FilePickerFileType("Audio") { Patterns = ["*.mp3", "*.wav", "*.aiff", "*.aif", "*.flac"] }
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

    [RelayCommand]
    private async Task SkipNextAsync()
    {
        await _audioPlaybackService.SkipNextAsync();
        NotifyCommandStatesChanged();
    }

    [RelayCommand]
    private async Task SkipPreviousAsync()
    {
        await _audioPlaybackService.SkipPreviousAsync();
        NotifyCommandStatesChanged();
    }

    [RelayCommand]
    private async Task PlaySelectedQueueItemAsync()
    {
        if (SelectedQueueItem is null)
            return;

        var index = QueueItems.IndexOf(SelectedQueueItem);
        if (index < 0)
            return;

        await _audioPlaybackService.PlayQueueItemAsync(index);
        NotifyCommandStatesChanged();
    }

    [RelayCommand]
    private void RemoveSelectedQueueItem()
    {
        if (SelectedQueueItem is null)
            return;

        var index = QueueItems.IndexOf(SelectedQueueItem);
        if (index < 0)
            return;

        _audioPlaybackService.RemoveFromQueue(index);
        SelectedQueueItem = null;
        NotifyCommandStatesChanged();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        _audioPlaybackService.ClearQueue();
        SelectedQueueItem = null;
        NotifyCommandStatesChanged();
    }

    [RelayCommand]
    private async Task ToggleSoundboardAsync()
    {
        IsSoundboardVisible = !IsSoundboardVisible;
        try
        {
            var uiSettings = await _settingsService.LoadUiSettingsAsync();
            uiSettings.IsSoundboardVisible = IsSoundboardVisible;
            await _settingsService.SaveUiSettingsAsync(uiSettings);
        }
        catch (Exception ex)
        {
            LogError("Failed to save soundboard panel visibility", ex);
        }
    }

    partial void OnIsSoundboardVisibleChanged(bool value) =>
        OnPropertyChanged(nameof(SoundboardToggleLabel));

    partial void OnVolumeChanged(double value)
    {
        _audioPlaybackService.Volume = AudioValues.HumanVolumeToFactor((float)value);
        ScheduleAudioSettingsSave();
    }

    partial void OnMasterVolumeChanged(double value)
    {
        _audioMixerService.MasterVolume = AudioValues.HumanVolumeToFactor((float)value);
        ScheduleAudioSettingsSave();
    }

    partial void OnSelectedOpusBitrateChanged(OpusBitrateOption? value)
    {
        if (_isLoadingSettings || value is null)
            return;

        _audioMixerService.EncoderBitrateKbps = value.Kbps;
        OpusBitrateDisplay = OpusBitratePresets.Label(_audioMixerService.EncoderBitrateKbps);
        _ = SaveAudioSettingsAsync();
    }

    partial void OnSelectedQueueItemChanged(PlaybackQueueItemViewModel? value) =>
        NotifyCommandStatesChanged();

    partial void OnIsConnectedChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnIsConnectingChanged(bool value) => NotifyCommandStatesChanged();
    partial void OnIsPlayingChanged(bool value)
    {
        NotifyCommandStatesChanged();
        UpdateProgressTimerState();
    }

    partial void OnIsPausedChanged(bool value)
    {
        NotifyCommandStatesChanged();
        UpdateProgressTimerState();
    }

    partial void OnHasQueueItemsChanged(bool value) => NotifyCommandStatesChanged();

    private void ScheduleAudioSettingsSave()
    {
        if (_isLoadingSettings)
            return;

        _volumeSaveDebounceCts?.Cancel();
        _volumeSaveDebounceCts?.Dispose();
        _volumeSaveDebounceCts = new CancellationTokenSource();
        _ = DebouncedSaveAudioSettingsAsync(_volumeSaveDebounceCts.Token);
    }

    private async Task DebouncedSaveAudioSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await SaveAudioSettingsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveAudioSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _settingsService.SaveAudioSettingsAsync(new AudioSettings
            {
                OpusBitrateKbps = _audioMixerService.EncoderBitrateKbps,
                MasterVolumeHuman = (int)Math.Round(MasterVolume),
                MusicVolumeHuman = (int)Math.Round(Volume)
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            LogError("Failed to save audio settings", ex);
        }
    }

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

            if (state == ConnectionState.Connected)
            {
                _nicknameService.SetBaseNickname(Nickname);
                _ = _nicknameService.ApplyInitialStatusAsync();
            }

            NotifyCommandStatesChanged();
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
            UpdateProgressTimerState();
            NotifyCommandStatesChanged();
        });
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(SyncPlaybackUiFromMixer);
    }

    private void OnNowPlayingChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SyncPlaybackUiFromMixer();
            ResetProgressDisplay();
        });
    }

    private void SyncPlaybackUiFromMixer()
    {
        RefreshQueue();
        UpdateNowPlayingDisplay();
        NotifyCommandStatesChanged();
    }

    private void RefreshQueue()
    {
        var queue = _audioMixerService.Queue;
        HasQueueItems = queue.Any(item =>
            item.Status is PlaybackQueueStatus.Queued or PlaybackQueueStatus.Playing);

        var selectedKey = SelectedQueueItem?.SourceKey;

        QueueItems.Clear();
        foreach (var item in queue)
            QueueItems.Add(new PlaybackQueueItemViewModel(item));

        SelectedQueueItem = selectedKey is null
            ? null
            : QueueItems.FirstOrDefault(i => i.SourceKey == selectedKey);

        CurrentlyPlayingItem = QueueItems.FirstOrDefault(i => i.IsCurrentlyPlaying);
    }

    private void UpdateNowPlayingDisplay()
    {
        NowPlayingDisplay = _audioMixerService.NowPlaying?.DisplayName ?? "Nothing playing";
        HasActiveTrack = _audioMixerService.NowPlaying is not null;
    }

    private void UpdateProgressTimerState()
    {
        if (IsPlaying)
            _progressTimer.Start();
        else
            _progressTimer.Stop();
    }

    private void ResetProgressDisplay()
    {
        if (_audioMixerService.NowPlaying is null)
        {
            ProgressValue = 0;
            ElapsedDisplay = "0:00";
            RemainingDisplay = "-0:00";
            HasActiveTrack = false;
            return;
        }

        UpdateProgressDisplay();
    }

    private void UpdateProgressDisplay()
    {
        var elapsed = _audioPlaybackService.CurrentPosition;
        var total = _audioPlaybackService.TotalDuration;

        ElapsedDisplay = FormatPlaybackTime(elapsed);
        RemainingDisplay = total > TimeSpan.Zero
            ? $"-{FormatPlaybackTime(total - elapsed)}"
            : "-0:00";

        ProgressValue = total > TimeSpan.Zero
            ? Math.Clamp(elapsed.TotalMilliseconds / total.TotalMilliseconds * 100.0, 0, 100)
            : 0;
    }

    internal static string FormatPlaybackTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
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
        OnPropertyChanged(nameof(CanSkipNext));
        OnPropertyChanged(nameof(CanSkipPrevious));
        OnPropertyChanged(nameof(CanRemoveQueueItem));
        OnPropertyChanged(nameof(CanClearQueue));
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _progressTimer.Stop();
        _volumeSaveDebounceCts?.Cancel();
        _volumeSaveDebounceCts?.Dispose();

        _teamSpeakService.StateChanged -= OnConnectionStateChanged;
        _teamSpeakService.StatusMessage -= OnStatusMessage;
        _audioPlaybackService.StateChanged -= OnPlaybackStateChanged;
        _audioMixerService.QueueChanged -= OnQueueChanged;
        _audioMixerService.NowPlayingChanged -= OnNowPlayingChanged;
        _audioMixerService.EncoderBitrateChanged -= OnEncoderBitrateChanged;
        _logService.EntryAdded -= OnLogEntryAdded;
        Soundboard.Dispose();
    }
}
