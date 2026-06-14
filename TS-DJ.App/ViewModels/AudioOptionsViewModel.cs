using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class AudioOptionsViewModel : ViewModelBase
{
    private readonly ILogger<AudioOptionsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly IAudioMixerService _audioMixerService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isLoading;

    [ObservableProperty]
    private double _masterVolume = 50;

    [ObservableProperty]
    private double _musicVolume = 50;

    [ObservableProperty]
    private OpusBitrateOption? _selectedOpusBitrate;

    public ObservableCollection<OpusBitrateOption> OpusBitrateOptions { get; } =
    [
        new(OpusBitratePresets.Low, OpusBitratePresets.Label(OpusBitratePresets.Low)),
        new(OpusBitratePresets.Medium, OpusBitratePresets.Label(OpusBitratePresets.Medium)),
        new(OpusBitratePresets.High, OpusBitratePresets.Label(OpusBitratePresets.High)),
        new(OpusBitratePresets.VeryHigh, OpusBitratePresets.Label(OpusBitratePresets.VeryHigh))
    ];

    public AudioOptionsViewModel(
        ILogger<AudioOptionsViewModel> logger,
        ISettingsService settingsService,
        IAudioMixerService audioMixerService,
        IAudioPlaybackService audioPlaybackService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _audioMixerService = audioMixerService;
        _audioPlaybackService = audioPlaybackService;
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsService.LoadAudioSettingsAsync();
            MasterVolume = settings.MasterVolumeHuman;
            MusicVolume = settings.MusicVolumeHuman;
            SelectedOpusBitrate = OpusBitrateOptions.FirstOrDefault(o => o.Kbps == settings.OpusBitrateKbps)
                ?? OpusBitrateOptions[1];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load audio settings");
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnMasterVolumeChanged(double value)
    {
        if (_isLoading)
            return;

        _audioMixerService.MasterVolume = AudioValues.HumanVolumeToFactor((float)value);
        ScheduleSave();
    }

    partial void OnMusicVolumeChanged(double value)
    {
        if (_isLoading)
            return;

        _audioPlaybackService.Volume = AudioValues.HumanVolumeToFactor((float)value);
        ScheduleSave();
    }

    partial void OnSelectedOpusBitrateChanged(OpusBitrateOption? value)
    {
        if (_isLoading || value is null)
            return;

        _audioMixerService.EncoderBitrateKbps = value.Kbps;
        ScheduleSave();
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
            await _settingsService.SaveAudioSettingsAsync(new AudioSettings
            {
                OpusBitrateKbps = _audioMixerService.EncoderBitrateKbps,
                MasterVolumeHuman = (int)Math.Round(MasterVolume),
                MusicVolumeHuman = (int)Math.Round(MusicVolume)
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save audio settings");
        }
    }
}
