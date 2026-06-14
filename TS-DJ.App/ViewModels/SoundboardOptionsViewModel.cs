using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.App.Views;
using TS_DJ.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class SoundboardOptionsViewModel : ViewModelBase
{
    private readonly ILogger<SoundboardOptionsViewModel> _logger;
    private readonly ISoundboardService _soundboardService;
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isLoading;

    [ObservableProperty]
    private double _volume = 50;

    public ObservableCollection<SoundboardPadViewModel> Pads { get; } = [];

    public SoundboardOptionsViewModel(
        ILogger<SoundboardOptionsViewModel> logger,
        ISoundboardService soundboardService)
    {
        _logger = logger;
        _soundboardService = soundboardService;

        for (var i = 0; i < SoundboardSettings.PadCount; i++)
            Pads.Add(new SoundboardPadViewModel { Index = i, Label = $"Pad {i + 1}" });
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            await _soundboardService.LoadAsync();
            Volume = AudioValues.FactorToHumanVolume(_soundboardService.Volume);

            foreach (var padModel in _soundboardService.Pads)
            {
                if (padModel.Index < 0 || padModel.Index >= Pads.Count)
                    continue;

                var vm = Pads[padModel.Index];
                vm.Label = padModel.Label;
                vm.FilePath = padModel.FilePath;
                vm.Hotkey = padModel.Hotkey;
                vm.GainHuman = padModel.GainHuman;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load soundboard settings");
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (_isLoading)
            return;

        _soundboardService.Volume = AudioValues.HumanVolumeToFactor((float)value);
        ScheduleSave();
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
        var teamSpeak = App.Services.GetRequiredService<ITeamSpeakService>();
        var viewModel = new SoundboardPadSettingsViewModel(
            logger,
            _soundboardService,
            teamSpeak,
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

    private void ScheduleSave()
    {
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
}
