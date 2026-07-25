using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TS_DJ.App.Services;
using TS_DJ.Core;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class UiPreferencesOptionsViewModel : ViewModelBase
{
    private readonly ILogger<UiPreferencesOptionsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly UpdatePromptService _updatePromptService;
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSoundboardVisible = true;

    [ObservableProperty]
    private string _applicationVersion = AppVersion.Display;

    [ObservableProperty]
    private string _updateStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    public UiPreferencesOptionsViewModel(
        ILogger<UiPreferencesOptionsViewModel> logger,
        ISettingsService settingsService,
        UpdatePromptService updatePromptService)
    {
        _logger = logger;
        _settingsService = settingsService;
        _updatePromptService = updatePromptService;
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsService.LoadUiSettingsAsync();
            IsSoundboardVisible = settings.IsSoundboardVisible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load UI preferences");
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnIsSoundboardVisibleChanged(bool value)
    {
        if (_isLoading)
            return;

        _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            var settings = await _settingsService.LoadUiSettingsAsync();
            settings.IsSoundboardVisible = IsSoundboardVisible;
            await _settingsService.SaveUiSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save UI preferences");
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdates)
            return;

        IsCheckingForUpdates = true;
        UpdateStatusMessage = "Checking for updates...";

        try
        {
            var message = await _updatePromptService.CheckManuallyAsync();
            UpdateStatusMessage = message ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual update check failed");
            UpdateStatusMessage = $"Update check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }
}
