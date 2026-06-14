using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class UiPreferencesOptionsViewModel : ViewModelBase
{
    private readonly ILogger<UiPreferencesOptionsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSoundboardVisible = true;

    public UiPreferencesOptionsViewModel(
        ILogger<UiPreferencesOptionsViewModel> logger,
        ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
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
}
