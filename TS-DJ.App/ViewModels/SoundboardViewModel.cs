using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using TS_DJ.App.Views;
using TS_DJ.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.TeamSpeak;

namespace TS_DJ.App.ViewModels;

public partial class SoundboardViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<SoundboardViewModel> _logger;
    private readonly ISoundboardService _soundboardService;
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly Dictionary<int, int> _padPlayRefCounts = [];
    private readonly Dictionary<int, List<DispatcherTimer>> _padCompletionTimers = [];
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isLoadingSettings;
    private bool _disposed;

    [ObservableProperty]
    private double _volume = 50;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int? _capturingHotkeyPadIndex;

    public bool IsCapturingHotkey => CapturingHotkeyPadIndex is not null;

    public ObservableCollection<SoundboardPadViewModel> Pads { get; } = [];

    public SoundboardViewModel(
        ILogger<SoundboardViewModel> logger,
        ISoundboardService soundboardService,
        ITeamSpeakService teamSpeakService)
    {
        _logger = logger;
        _soundboardService = soundboardService;
        _teamSpeakService = teamSpeakService;

        _soundboardService.PadsChanged += OnPadsChanged;
        _soundboardService.PadTriggered += OnPadTriggered;
        _teamSpeakService.StateChanged += OnConnectionStateChanged;

        for (var i = 0; i < SoundboardSettings.PadCount; i++)
            Pads.Add(new SoundboardPadViewModel { Index = i, Label = $"Pad {i + 1}" });

        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        _isLoadingSettings = true;
        try
        {
            await _soundboardService.LoadAsync();
            SyncPadsFromService();
            Volume = AudioValues.FactorToHumanVolume(_soundboardService.Volume);
            IsConnected = _teamSpeakService.State == ConnectionState.Connected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load soundboard settings");
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    public bool TryHandleHotkey(Key key, KeyModifiers modifiers)
    {
        if (CapturingHotkeyPadIndex is int captureIndex)
        {
            if (key is Key.Escape)
            {
                CapturingHotkeyPadIndex = null;
                return true;
            }

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return true;

            var hotkey = HotkeyFormatter.Format(key, modifiers);
            _soundboardService.SetPadHotkey(captureIndex, hotkey);
            CapturingHotkeyPadIndex = null;
            ScheduleSave();
            return true;
        }

        var hotkeyString = HotkeyFormatter.Format(key, modifiers);
        var padIndex = _soundboardService.FindPadByHotkey(hotkeyString);
        if (padIndex is null)
            return false;

        PlayPad(padIndex.Value);
        return true;
    }

    [RelayCommand]
    private void PlayPad(SoundboardPadViewModel? pad)
    {
        if (pad is null || !pad.CanPlay)
            return;

        PlayPad(pad.Index);
    }

    private void PlayPad(int index)
    {
        _soundboardService.PlayPad(index);
    }

    [RelayCommand]
    private async Task AssignPadAsync(SoundboardPadViewModel? pad)
    {
        if (pad is null)
            return;

        var path = await PickAudioFileAsync();
        if (path is null)
            return;

        try
        {
            _soundboardService.AssignPad(pad.Index, path);
            ScheduleSave();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign pad {Index}", pad.Index);
        }
    }

    [RelayCommand]
    private void ClearPad(SoundboardPadViewModel? pad)
    {
        if (pad is null)
            return;

        _soundboardService.ClearPad(pad.Index);
        ScheduleSave();
    }

    [RelayCommand]
    private void StartHotkeyCapture(SoundboardPadViewModel? pad)
    {
        if (pad is null)
            return;

        CapturingHotkeyPadIndex = pad.Index;
    }

    [RelayCommand]
    private void OpenPadSettings(SoundboardPadViewModel? pad)
    {
        if (pad is null)
            return;

        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var logger = App.Services.GetRequiredService<ILogger<SoundboardPadSettingsViewModel>>();
        var viewModel = new SoundboardPadSettingsViewModel(
            logger,
            _soundboardService,
            _teamSpeakService,
            pad.Index);

        var window = new SoundboardPadSettingsWindow
        {
            DataContext = viewModel
        };

        if (desktop.MainWindow is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    partial void OnCapturingHotkeyPadIndexChanged(int? value) =>
        OnPropertyChanged(nameof(IsCapturingHotkey));

    partial void OnVolumeChanged(double value)
    {
        if (_isLoadingSettings)
            return;

        _soundboardService.Volume = AudioValues.HumanVolumeToFactor((float)value);
        ScheduleSave();
    }

    partial void OnIsConnectedChanged(bool value)
    {
        foreach (var pad in Pads)
            pad.IsConnected = value;
    }

    private void OnPadsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(SyncPadsFromService);

    private void SyncPadsFromService()
    {
        foreach (var padModel in _soundboardService.Pads)
        {
            if (padModel.Index < 0 || padModel.Index >= Pads.Count)
                continue;

            var vm = Pads[padModel.Index];
            vm.Label = padModel.Label;
            vm.FilePath = padModel.FilePath;
            vm.Hotkey = padModel.Hotkey;
            vm.GainHuman = padModel.GainHuman;
            vm.IsConnected = IsConnected;
        }
    }

    private void OnPadTriggered(object? sender, int index)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (index < 0 || index >= Pads.Count)
                return;

            var pad = Pads[index];
            string? filePath;
            lock (_padPlayRefCounts)
            {
                _padPlayRefCounts.TryGetValue(index, out var count);
                _padPlayRefCounts[index] = count + 1;
            }

            pad.IsPlaying = true;

            filePath = pad.FilePath;
            var durationSeconds = filePath is not null
                ? _soundboardService.GetEstimatedDurationSeconds(filePath)
                : 2.0;

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(0.1, durationSeconds))
            };

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                RemovePadPlaybackInstance(index, timer);
            };

            lock (_padCompletionTimers)
            {
                if (!_padCompletionTimers.TryGetValue(index, out var timers))
                {
                    timers = [];
                    _padCompletionTimers[index] = timers;
                }

                timers.Add(timer);
            }

            timer.Start();
        });
    }

    private void RemovePadPlaybackInstance(int index, DispatcherTimer timer)
    {
        if (index < 0 || index >= Pads.Count)
            return;

        lock (_padCompletionTimers)
        {
            if (_padCompletionTimers.TryGetValue(index, out var timers))
                timers.Remove(timer);
        }

        var remaining = 0;
        lock (_padPlayRefCounts)
        {
            if (_padPlayRefCounts.TryGetValue(index, out var count))
            {
                remaining = Math.Max(0, count - 1);
                if (remaining == 0)
                    _padPlayRefCounts.Remove(index);
                else
                    _padPlayRefCounts[index] = remaining;
            }
        }

        if (remaining == 0)
            Pads[index].IsPlaying = false;
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
        if (_isLoadingSettings)
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
            _logger.LogError(ex, "Failed to save soundboard settings");
        }
    }

    public async Task SaveForShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _soundboardService.SaveAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save soundboard settings during shutdown");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _soundboardService.PadsChanged -= OnPadsChanged;
        _soundboardService.PadTriggered -= OnPadTriggered;
        _teamSpeakService.StateChanged -= OnConnectionStateChanged;

        foreach (var timers in _padCompletionTimers.Values)
        {
            foreach (var timer in timers)
                timer.Stop();
        }

        _padCompletionTimers.Clear();
        _padPlayRefCounts.Clear();
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
    }
}
