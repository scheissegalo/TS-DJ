using Microsoft.Extensions.Logging;
using TS_DJ.App.ViewModels;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.TeamSpeak;

namespace TS_DJ.App.Services;

public sealed class ApplicationShutdownService
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<ApplicationShutdownService> _logger;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly TeamSpeakNicknameService _nicknameService;
    private readonly ISettingsService _settingsService;
    private readonly MainWindowViewModel _mainWindowViewModel;

    private int _isShuttingDown;

    public ApplicationShutdownService(
        ILogger<ApplicationShutdownService> logger,
        IAudioPlaybackService audioPlaybackService,
        ITeamSpeakService teamSpeakService,
        TeamSpeakNicknameService nicknameService,
        ISettingsService settingsService,
        MainWindowViewModel mainWindowViewModel)
    {
        _logger = logger;
        _audioPlaybackService = audioPlaybackService;
        _teamSpeakService = teamSpeakService;
        _nicknameService = nicknameService;
        _settingsService = settingsService;
        _mainWindowViewModel = mainWindowViewModel;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _isShuttingDown, 1, 0) != 0)
            return;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ShutdownTimeout);
        var token = timeoutCts.Token;

        _logger.LogInformation("Application shutdown started");

        await RunStepAsync("save settings", () => _mainWindowViewModel.SaveSettingsForShutdownAsync(token), token);
        await RunStepAsync("restore nickname", () => _nicknameService.RestoreBaseNicknameAsync(token), token);
        await RunStepAsync("stop playback", () => _audioPlaybackService.StopAsync(token), token);

        if (_teamSpeakService.State == ConnectionState.Connected)
            await RunStepAsync("disconnect TeamSpeak", () => _teamSpeakService.DisconnectAsync(token), token);

        _logger.LogInformation("Application shutdown completed");
    }

    public async Task SaveWindowSettingsAsync(
        double width,
        double height,
        double x,
        double y,
        string windowState,
        bool isSoundboardVisible,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var existing = await LoadWindowSettingsAsync(cancellationToken);
            await _settingsService.SaveUiSettingsAsync(new UiSettings
            {
                Width = width,
                Height = height,
                X = x,
                Y = y,
                WindowState = windowState,
                IsSoundboardVisible = isSoundboardVisible
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save window settings");
        }
    }

    public async Task<UiSettings> LoadWindowSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _settingsService.LoadUiSettingsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load window settings");
            return new UiSettings();
        }
    }

    private async Task RunStepAsync(string stepName, Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Shutdown step: {Step}", stepName);
            await action();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Shutdown step timed out or was cancelled: {Step}", stepName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shutdown step failed: {Step}", stepName);
        }
    }
}
