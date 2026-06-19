using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.App.Services;
using TS_DJ.Audio;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.Logging;
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
    private readonly IPlaylistService _playlistService;
    private readonly IPlaybackTargetService _playbackTarget;
    private readonly TeamSpeakNicknameService _nicknameService;
    private readonly TrackTransitionProfiler _transitionProfiler;
    private readonly DispatcherTimer _progressTimer;
    private CancellationTokenSource? _volumeSaveDebounceCts;
    private bool _isLoadingSettings;
    private bool _disposed;
    private bool _playbackUiSyncPending;
    private bool _playbackUiSyncNeedsProgressReset;
    private string? _lastPlayingSourceKeyForScroll;

    [ObservableProperty]
    private string _channel = string.Empty;

    [ObservableProperty]
    private TeamSpeakChannelInfo? _selectedChannel;

    [ObservableProperty]
    private bool _showEmptyChannels;

    private bool _isSyncingChannelSelection;

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
    private PlaybackMode _playbackMode = PlaybackMode.SequentialQueue;

    [ObservableProperty]
    private bool _crossfadeEnabled;

    [ObservableProperty]
    private double _crossfadeDurationSeconds = 4;

    [ObservableProperty]
    private DeckId _selectedLoadDeck = DeckId.A;

    public DeckId[] LoadDeckOptions { get; } = [DeckId.A, DeckId.B];

    public DeckViewModel DeckA { get; } = new(DeckId.A);
    public DeckViewModel DeckB { get; } = new(DeckId.B);

    public bool IsDualDeckMode => PlaybackMode == PlaybackMode.DualDeck;
    public bool IsSequentialMode => PlaybackMode == PlaybackMode.SequentialQueue;

    public PlaybackMode[] PlaybackModeOptions { get; } =
        [PlaybackMode.SequentialQueue, PlaybackMode.DualDeck];

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
    private bool _isSoundboardVisible = true;

    [ObservableProperty]
    private PlaybackQueueItemViewModel? _selectedQueueItem;

    [ObservableProperty]
    private PlaybackQueueItemViewModel? _currentlyPlayingItem;

    public ConnectionProfileSelectorViewModel ConnectionProfiles { get; }

    public ObservableCollection<SavedPlaylistSummary> SavedPlaylists { get; } = [];

    [ObservableProperty]
    private SavedPlaylistSummary? _selectedSavedPlaylist;

    public string SoundboardToggleLabel => IsSoundboardVisible ? "Hide Soundboard" : "Show Soundboard";

    public string ApplicationVersion { get; } =
        typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    public string WindowTitle => $"TS-DJ {ApplicationVersion}";

    public bool CanConnect => !IsConnected && !IsConnecting && ConnectionProfiles.SelectedProfile is not null;
    public bool CanDisconnect => IsConnected;
    public bool CanPlay => IsConnected && (
        PlaybackMode == PlaybackMode.DualDeck
            ? (DeckA.HasLoadedTrack || DeckB.HasLoadedTrack)
            : HasQueueItems && !IsPlaying && !IsPaused);

    public bool CanLoadSelectedToDeck =>
        IsConnected && IsDualDeckMode && SelectedQueueItem is not null;

    public bool CanPlayDeckA => IsConnected && IsDualDeckMode && DeckA.HasLoadedTrack;
    public bool CanPlayDeckB => IsConnected && IsDualDeckMode && DeckB.HasLoadedTrack;
    public bool CanCueDeckA => IsConnected && IsDualDeckMode && DeckA.HasLoadedTrack;
    public bool CanCueDeckB => IsConnected && IsDualDeckMode && DeckB.HasLoadedTrack;
    public bool CanPause => IsPlaying || _audioMixerService.DeckA.IsPlaying || _audioMixerService.DeckB.IsPlaying;
    public bool CanStop => IsPlaying || IsPaused || _audioMixerService.DeckA.IsPlaying || _audioMixerService.DeckB.IsPlaying;
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

    public ObservableCollection<TeamSpeakChannelInfo> AvailableChannels { get; } = [];

    public event EventHandler<LogEntry>? LogEntryAppended;

    public TrackTransitionProfiler TransitionProfiler => _transitionProfiler;

    public ObservableCollection<PlaybackQueueItemViewModel> QueueItems { get; } = [];

    public SoundboardViewModel Soundboard { get; }

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        ITeamSpeakService teamSpeakService,
        IAudioPlaybackService audioPlaybackService,
        IAudioMixerService audioMixerService,
        ISettingsService settingsService,
        ILogService logService,
        IPlaylistService playlistService,
        IPlaybackTargetService playbackTarget,
        TeamSpeakNicknameService nicknameService,
        TrackTransitionProfiler transitionProfiler,
        SoundboardViewModel soundboardViewModel,
        ConnectionProfileSelectorViewModel connectionProfileSelectorViewModel)
    {
        _logger = logger;
        _teamSpeakService = teamSpeakService;
        Soundboard = soundboardViewModel;
        ConnectionProfiles = connectionProfileSelectorViewModel;
        _audioPlaybackService = audioPlaybackService;
        _audioMixerService = audioMixerService;
        _settingsService = settingsService;
        _logService = logService;
        _playlistService = playlistService;
        _playbackTarget = playbackTarget;
        _nicknameService = nicknameService;
        _transitionProfiler = transitionProfiler;

        ConnectionProfiles.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ConnectionProfileSelectorViewModel.SelectedProfile))
            {
                if (!IsConnected)
                    Channel = ConnectionProfiles.GetDefaultChannel();
                NotifyCommandStatesChanged();
            }
        };

        _teamSpeakService.StateChanged += OnConnectionStateChanged;
        _teamSpeakService.StatusMessage += OnStatusMessage;
        _teamSpeakService.ChannelsUpdated += OnChannelsUpdated;
        _teamSpeakService.CurrentChannelChanged += OnCurrentChannelChanged;
        _audioPlaybackService.StateChanged += OnPlaybackStateChanged;
        _audioMixerService.QueueChanged += OnQueueChanged;
        _audioMixerService.NowPlayingChanged += OnNowPlayingChanged;
        _audioMixerService.AdvancePendingChanged += OnAdvancePendingChanged;
        _audioMixerService.DeckStateChanged += OnDeckStateChanged;
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
        SyncDeckDisplays();

        _ = LoadSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        _isLoadingSettings = true;
        try
        {
            await ConnectionProfiles.LoadProfilesAsync();
            Channel = ConnectionProfiles.GetDefaultChannel();

            var audioSettings = await _settingsService.LoadAudioSettingsAsync();
            Volume = audioSettings.MusicVolumeHuman;
            MasterVolume = audioSettings.MasterVolumeHuman;
            _audioPlaybackService.Volume = AudioValues.HumanVolumeToFactor((float)Volume);
            _audioMixerService.MasterVolume = AudioValues.HumanVolumeToFactor((float)MasterVolume);
            _audioMixerService.EncoderBitrateKbps = audioSettings.OpusBitrateKbps;

            var uiSettings = await _settingsService.LoadUiSettingsAsync();
            IsSoundboardVisible = uiSettings.IsSoundboardVisible;

            var playbackSettings = await _settingsService.LoadPlaybackSettingsAsync();
            PlaybackMode = playbackSettings.Mode;
            CrossfadeEnabled = playbackSettings.CrossfadeEnabled;
            CrossfadeDurationSeconds = playbackSettings.CrossfadeDurationSeconds;

            await RefreshSavedPlaylistsAsync();
        }
        catch (Exception ex)
        {
            LogError("Failed to load saved settings", ex);
        }
        finally
        {
            _isLoadingSettings = false;
            ApplyPlaybackSettings();
            SyncDeckDisplays();
            _playbackTarget.SelectedLoadDeck = SelectedLoadDeck;
        }
    }

    public async Task RefreshSavedPlaylistsAsync()
    {
        try
        {
            var items = await _playlistService.ListAsync();
            SavedPlaylists.Clear();
            foreach (var item in items)
                SavedPlaylists.Add(item);
        }
        catch (Exception ex)
        {
            LogError("Failed to load saved playlists", ex);
        }
    }

    public async Task SaveSettingsForShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveAudioSettingsAsync(cancellationToken);
            await Soundboard.SaveForShutdownAsync(cancellationToken);
            await ConnectionProfiles.UpdateProfileDefaultChannelAsync(Channel, cancellationToken);
        }
        catch (Exception ex)
        {
            LogError("Failed to save settings during shutdown", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task RefreshChannelsAsync()
    {
        try
        {
            var channels = await _teamSpeakService.GetChannelsAsync(ShowEmptyChannels);
            ApplyAvailableChannels(channels);
            SyncSelectedChannelFromCurrent();
        }
        catch (Exception ex)
        {
            LogError("Failed to refresh channels", ex);
        }
    }

    partial void OnShowEmptyChannelsChanged(bool value)
    {
        if (!IsConnected)
            return;

        _ = RefreshChannelsAsync();
    }

    partial void OnSelectedChannelChanged(TeamSpeakChannelInfo? value)
    {
        if (_isSyncingChannelSelection || value is null)
            return;

        Channel = value.Id;

        if (!IsConnected)
            return;

        _ = MoveToSelectedChannelAsync(value);
    }

    private async Task MoveToSelectedChannelAsync(TeamSpeakChannelInfo channel)
    {
        try
        {
            await _teamSpeakService.MoveToChannelAsync(channel.Id);
            await PersistChannelSettingsAsync();
        }
        catch (Exception ex)
        {
            LogError($"Failed to move to channel {channel.DisplayPath}", ex);
        }
    }

    private async Task PersistChannelSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ConnectionProfiles.UpdateProfileDefaultChannelAsync(Channel, cancellationToken);
        }
        catch (Exception ex)
        {
            LogError("Failed to save channel settings", ex);
        }
    }

    private void ApplyAvailableChannels(IReadOnlyList<TeamSpeakChannelInfo> channels)
    {
        AvailableChannels.Clear();
        foreach (var channel in channels)
            AvailableChannels.Add(channel);
    }

    private void SyncSelectedChannelFromCurrent()
    {
        var current = _teamSpeakService.GetCurrentChannel();
        if (current is null)
            return;

        _isSyncingChannelSelection = true;
        try
        {
            SelectedChannel = AvailableChannels.FirstOrDefault(c => c.Id == current.Id)
                ?? AvailableChannels.FirstOrDefault(c =>
                    string.Equals(c.DisplayPath, current.DisplayPath, StringComparison.OrdinalIgnoreCase))
                ?? current;
            Channel = current.Id;
        }
        finally
        {
            _isSyncingChannelSelection = false;
        }
    }

    private void OnChannelsUpdated(object? sender, IReadOnlyList<TeamSpeakChannelInfo> channels)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyAvailableChannels(channels);
            SyncSelectedChannelFromCurrent();
        });
    }

    private void OnCurrentChannelChanged(object? sender, TeamSpeakChannelInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isSyncingChannelSelection = true;
            try
            {
                Channel = info.Id;
                var match = AvailableChannels.FirstOrDefault(c => c.Id == info.Id);
                if (match is not null)
                {
                    SelectedChannel = match;
                }
                else
                {
                    AvailableChannels.Add(info);
                    SelectedChannel = info;
                }
            }
            finally
            {
                _isSyncingChannelSelection = false;
            }

            _ = PersistChannelSettingsAsync();
        });
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (ConnectionProfiles.SelectedProfile is null)
            return;

        try
        {
            var settings = ConnectionProfiles.ToConnectionSettings(Channel);
            _nicknameService.SetBaseNickname(settings.Nickname);
            await ConnectionProfiles.PersistSelectedProfileIdAsync();
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
    private void OpenOptions()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var viewModel = App.Services.GetRequiredService<OptionsViewModel>();
        var window = new Views.OptionsWindow
        {
            DataContext = viewModel
        };

        window.Closed += async (_, _) =>
        {
            await ConnectionProfiles.LoadProfilesAsync();
            try
            {
                var uiSettings = await _settingsService.LoadUiSettingsAsync();
                IsSoundboardVisible = uiSettings.IsSoundboardVisible;
            }
            catch (Exception ex)
            {
                LogError("Failed to reload UI settings after Options", ex);
            }
        };

        if (desktop.MainWindow is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    [RelayCommand]
    private void OpenOptionsTeamSpeak()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var viewModel = App.Services.GetRequiredService<OptionsViewModel>();
        viewModel.OpenWithSectionCommand.Execute(OptionsSection.TeamSpeakConnections);

        var window = new Views.OptionsWindow
        {
            DataContext = viewModel
        };

        window.Closed += async (_, _) =>
        {
            await ConnectionProfiles.LoadProfilesAsync();
            try
            {
                var uiSettings = await _settingsService.LoadUiSettingsAsync();
                IsSoundboardVisible = uiSettings.IsSoundboardVisible;
            }
            catch (Exception ex)
            {
                LogError("Failed to reload UI settings after Options", ex);
            }
        };

        if (desktop.MainWindow is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    [RelayCommand]
    private async Task SavePlaylistAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        if (desktop.MainWindow is not Window owner)
            return;

        var dialogVm = new SavePlaylistDialogViewModel();
        var dialog = new Views.SavePlaylistDialog
        {
            DataContext = dialogVm
        };

        var result = await dialog.ShowDialog<bool>(owner);
        if (!result)
            return;

        try
        {
            var queue = _audioMixerService.Queue.ToList();
            await _playlistService.SaveFromQueueAsync(dialogVm.Name.Trim(), queue, dialogVm.Description);
            await RefreshSavedPlaylistsAsync();
            _logService.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "Info",
                Message = $"Saved playlist '{dialogVm.Name.Trim()}' ({queue.Count} tracks)"
            });
        }
        catch (Exception ex)
        {
            LogError("Failed to save playlist", ex);
        }
    }

    [RelayCommand]
    private async Task LoadPlaylistAsync(SavedPlaylistSummary? summary)
    {
        if (summary is null)
            return;

        await LoadPlaylistInternalAsync(summary, replaceQueue: true, playFirst: false);
    }

    [RelayCommand]
    private async Task AppendPlaylistAsync(SavedPlaylistSummary? summary)
    {
        if (summary is null)
            return;

        await LoadPlaylistInternalAsync(summary, replaceQueue: false, playFirst: false);
    }

    [RelayCommand]
    private async Task PlayPlaylistNowAsync(SavedPlaylistSummary? summary)
    {
        if (summary is null)
            return;

        await LoadPlaylistInternalAsync(summary, replaceQueue: true, playFirst: true);
    }

    private async Task LoadPlaylistInternalAsync(SavedPlaylistSummary summary, bool replaceQueue, bool playFirst)
    {
        try
        {
            var playlist = await _playlistService.GetAsync(summary.Id);
            if (playlist is null)
            {
                LogError($"Playlist '{summary.Name}' not found", new InvalidOperationException("Missing playlist"));
                return;
            }

            var items = _playlistService.ResolveEntries(playlist);
            if (replaceQueue)
                _audioMixerService.ClearQueue();

            _audioMixerService.EnqueueRange(items);

            if (playFirst && items.Count > 0)
                await _audioPlaybackService.PlayQueueItemAsync(0);

            _logService.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "Info",
                Message = $"{(replaceQueue ? "Loaded" : "Appended")} playlist '{summary.Name}' ({items.Count} tracks)"
            });
        }
        catch (Exception ex)
        {
            LogError($"Failed to load playlist '{summary.Name}'", ex);
        }
    }

    [RelayCommand]
    private async Task OpenPlaylistManagerAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var viewModel = App.Services.GetRequiredService<PlaylistManagerViewModel>();
        var dialog = new Views.PlaylistManagerDialog
        {
            DataContext = viewModel
        };

        if (desktop.MainWindow is Window owner)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        await RefreshSavedPlaylistsAsync();
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
    private void OpenYouTubeDialog()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (desktop.MainWindow is not Window owner)
            return;

        var viewModel = App.Services.GetRequiredService<YouTubeUrlDialogViewModel>();
        var dialog = new Views.YouTubeUrlDialog
        {
            DataContext = viewModel
        };

        dialog.ShowDialog(owner);
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
            if (IsDualDeckMode)
            {
                await _playbackTarget.LoadItemAsync(
                    PlaybackQueueItem.FromLocalFile(path),
                    playImmediately: false);
            }
            else
            {
                await _audioPlaybackService.LoadAsync(path);
            }

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

    [RelayCommand(CanExecute = nameof(CanLoadSelectedToDeck))]
    private async Task LoadSelectedToDeckAAsync() =>
        await LoadSelectedToDeckAsync(DeckId.A);

    [RelayCommand(CanExecute = nameof(CanLoadSelectedToDeck))]
    private async Task LoadSelectedToDeckBAsync() =>
        await LoadSelectedToDeckAsync(DeckId.B);

    [RelayCommand(CanExecute = nameof(CanPlayDeckA))]
    private async Task PlayDeckAAsync() => await _audioPlaybackService.PlayDeckAsync(DeckId.A);

    [RelayCommand(CanExecute = nameof(CanPlayDeckB))]
    private async Task PlayDeckBAsync() => await _audioPlaybackService.PlayDeckAsync(DeckId.B);

    [RelayCommand(CanExecute = nameof(CanCueDeckA))]
    private void CueDeckA()
    {
        _audioPlaybackService.CueDeck(DeckId.A);
        SyncDeckDisplays();
        NotifyCommandStatesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCueDeckB))]
    private void CueDeckB()
    {
        _audioPlaybackService.CueDeck(DeckId.B);
        SyncDeckDisplays();
        NotifyCommandStatesChanged();
    }

    private async Task LoadSelectedToDeckAsync(DeckId deckId)
    {
        if (SelectedQueueItem is null)
            return;

        var item = _audioMixerService.Queue.FirstOrDefault(i => i.SourceKey == SelectedQueueItem.SourceKey);
        if (item is null)
            return;

        try
        {
            await _audioPlaybackService.LoadToDeckAsync(deckId, CloneQueueItem(item));
            SyncDeckDisplays();
            NotifyCommandStatesChanged();
        }
        catch (Exception ex)
        {
            LogError($"Failed to load track to deck {deckId}", ex);
        }
    }

    private static PlaybackQueueItem CloneQueueItem(PlaybackQueueItem item) =>
        new()
        {
            SourceKind = item.SourceKind,
            FilePath = item.FilePath,
            RemoteTrackId = item.RemoteTrackId,
            VideoUrl = item.VideoUrl,
            ThumbnailUrl = item.ThumbnailUrl,
            DisplayName = item.DisplayName,
            Artist = item.Artist,
            Album = item.Album,
            DurationSeconds = item.DurationSeconds,
            Status = item.Status
        };

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

    partial void OnSelectedQueueItemChanged(PlaybackQueueItemViewModel? value) =>
        NotifyCommandStatesChanged();

    partial void OnIsConnectedChanged(bool value)
    {
        RefreshChannelsCommand.NotifyCanExecuteChanged();
        NotifyCommandStatesChanged();
    }
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

    partial void OnPlaybackModeChanged(PlaybackMode value)
    {
        OnPropertyChanged(nameof(IsDualDeckMode));
        OnPropertyChanged(nameof(IsSequentialMode));
        ApplyPlaybackSettings();
        NotifyCommandStatesChanged();
    }

    partial void OnCrossfadeEnabledChanged(bool value) => ApplyPlaybackSettings();

    partial void OnCrossfadeDurationSecondsChanged(double value) => ApplyPlaybackSettings();

    partial void OnSelectedLoadDeckChanged(DeckId value) =>
        _playbackTarget.SelectedLoadDeck = value;

    private void ApplyPlaybackSettings()
    {
        if (_isLoadingSettings)
            return;

        var settings = new PlaybackSettings
        {
            Mode = PlaybackMode,
            CrossfadeEnabled = CrossfadeEnabled,
            CrossfadeDurationSeconds = CrossfadeDurationSeconds
        };

        _audioPlaybackService.SetPlaybackSettings(settings);
        _ = SavePlaybackSettingsAsync();
    }

    private async Task SavePlaybackSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _settingsService.SavePlaybackSettingsAsync(new PlaybackSettings
            {
                Mode = PlaybackMode,
                CrossfadeEnabled = CrossfadeEnabled,
                CrossfadeDurationSeconds = CrossfadeDurationSeconds
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            LogError("Failed to save playback settings", ex);
        }
    }

    private void OnDeckStateChanged(object? sender, DeckStateChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            SyncDeckDisplays();
            if (IsDualDeckMode && (DeckA.IsPlaying || DeckB.IsPlaying))
            {
                IsPlaying = true;
                IsPaused = false;
                PlaybackStatus = "Playing";
            }

            NotifyCommandStatesChanged();
        });

    private void SyncDeckDisplays()
    {
        DeckA.UpdateFrom(_audioMixerService.DeckA.LoadedItem, _audioMixerService.DeckA.IsPlaying);
        DeckB.UpdateFrom(_audioMixerService.DeckB.LoadedItem, _audioMixerService.DeckB.IsPlaying);
        NotifyCommandStatesChanged();
    }

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
                _nicknameService.SetBaseNickname(ConnectionProfiles.SelectedNickname);
                _ = _nicknameService.ApplyInitialStatusAsync();
            }
            else if (state == ConnectionState.Disconnected || state == ConnectionState.Error)
            {
                AvailableChannels.Clear();
                SelectedChannel = null;
            }

            RefreshChannelsCommand.NotifyCanExecuteChanged();
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

    private void OnQueueChanged(object? sender, EventArgs e) =>
        SchedulePlaybackUiSync();

    private void OnNowPlayingChanged(object? sender, EventArgs e) =>
        SchedulePlaybackUiSync(resetProgress: true);

    private void OnAdvancePendingChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdatePlaybackStatusDisplay);

    private void UpdatePlaybackStatusDisplay()
    {
        if (_audioMixerService.AdvancePending
            && (_audioMixerService.DeckA.IsPlaying || _audioMixerService.DeckB.IsPlaying))
        {
            PlaybackStatus = "Preparing next track…";
            return;
        }

        if (IsPlaying)
            PlaybackStatus = "Playing";
        else if (IsPaused)
            PlaybackStatus = "Paused";
        else
            PlaybackStatus = "Stopped";
    }

    private void SchedulePlaybackUiSync(bool resetProgress = false)
    {
        _playbackUiSyncNeedsProgressReset |= resetProgress;

        if (_playbackUiSyncPending)
            return;

        _playbackUiSyncPending = true;
        Dispatcher.UIThread.Post(RunCoalescedPlaybackUiSync, DispatcherPriority.Background);
    }

    private void RunCoalescedPlaybackUiSync()
    {
        _playbackUiSyncPending = false;
        var resetProgress = _playbackUiSyncNeedsProgressReset;
        _playbackUiSyncNeedsProgressReset = false;

        var newPlayingKey = _audioMixerService.NowPlaying?.SourceKey;
        if (!string.IsNullOrEmpty(newPlayingKey) && newPlayingKey != _lastPlayingSourceKeyForScroll)
            _transitionProfiler.BeginTransition(newPlayingKey);

        var queueSw = Stopwatch.StartNew();
        SyncPlaybackUiFromMixer();
        _transitionProfiler.RecordQueueSync(queueSw.Elapsed);

        if (resetProgress)
            ResetProgressDisplay();
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

        if (TryUpdateQueueInPlace(queue))
        {
            SelectedQueueItem = selectedKey is null
                ? null
                : QueueItems.FirstOrDefault(i => i.SourceKey == selectedKey);

            UpdateCurrentlyPlayingItem();
            return;
        }

        RebuildQueue(queue, selectedKey);
    }

    private bool TryUpdateQueueInPlace(IReadOnlyList<PlaybackQueueItem> queue)
    {
        if (QueueItems.Count != queue.Count)
            return false;

        for (var i = 0; i < queue.Count; i++)
        {
            if (QueueItems[i].SourceKey != queue[i].SourceKey)
                return false;
        }

        foreach (var (viewModel, item) in QueueItems.Zip(queue))
            viewModel.UpdateFrom(item);

        return true;
    }

    private void RebuildQueue(IReadOnlyList<PlaybackQueueItem> queue, string? selectedKey)
    {
        QueueItems.Clear();
        foreach (var item in queue)
            QueueItems.Add(new PlaybackQueueItemViewModel(item));

        SelectedQueueItem = selectedKey is null
            ? null
            : QueueItems.FirstOrDefault(i => i.SourceKey == selectedKey);

        UpdateCurrentlyPlayingItem();
    }

    private void UpdateCurrentlyPlayingItem()
    {
        var playing = QueueItems.FirstOrDefault(i => i.IsCurrentlyPlaying);
        var playingKey = playing?.SourceKey;

        if (playingKey == _lastPlayingSourceKeyForScroll)
            return;

        _lastPlayingSourceKeyForScroll = playingKey;
        CurrentlyPlayingItem = playing;
        _logger.LogDebug("Queue scroll target changed to {SourceKey}", playingKey ?? "none");
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

    private void OnLogEntryAdded(object? sender, LogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > LogService.MaxLogEntries)
                LogEntries.RemoveAt(0);

            LogEntryAppended?.Invoke(this, entry);
        });
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
        OnPropertyChanged(nameof(CanLoadSelectedToDeck));
        OnPropertyChanged(nameof(CanPlayDeckA));
        OnPropertyChanged(nameof(CanPlayDeckB));
        OnPropertyChanged(nameof(CanCueDeckA));
        OnPropertyChanged(nameof(CanCueDeckB));
        LoadSelectedToDeckACommand.NotifyCanExecuteChanged();
        LoadSelectedToDeckBCommand.NotifyCanExecuteChanged();
        PlayDeckACommand.NotifyCanExecuteChanged();
        PlayDeckBCommand.NotifyCanExecuteChanged();
        CueDeckACommand.NotifyCanExecuteChanged();
        CueDeckBCommand.NotifyCanExecuteChanged();
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
        _teamSpeakService.ChannelsUpdated -= OnChannelsUpdated;
        _teamSpeakService.CurrentChannelChanged -= OnCurrentChannelChanged;
        _audioPlaybackService.StateChanged -= OnPlaybackStateChanged;
        _audioMixerService.QueueChanged -= OnQueueChanged;
        _audioMixerService.NowPlayingChanged -= OnNowPlayingChanged;
        _audioMixerService.AdvancePendingChanged -= OnAdvancePendingChanged;
        _audioMixerService.DeckStateChanged -= OnDeckStateChanged;
        _logService.EntryAdded -= OnLogEntryAdded;
        Soundboard.Dispose();
    }
}
