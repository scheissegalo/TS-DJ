using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class NavidromeOptionsViewModel : ViewModelBase
{
    private readonly ILogger<NavidromeOptionsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isLoading;

    [ObservableProperty]
    private string _serverUrl = "http://10.0.0.1:4533";

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    public NavidromeOptionsViewModel(
        ILogger<NavidromeOptionsViewModel> logger,
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
            var settings = await _settingsService.LoadNavidromeSettingsAsync();
            ServerUrl = settings.ServerUrl;
            Username = settings.Username;
            Password = settings.Password;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Navidrome settings");
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnServerUrlChanged(string value) => ScheduleSave();
    partial void OnUsernameChanged(string value) => ScheduleSave();
    partial void OnPasswordChanged(string value) => ScheduleSave();

    private void ScheduleSave()
    {
        if (_isLoading)
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
            await _settingsService.SaveNavidromeSettingsAsync(new NavidromeSettings
            {
                ServerUrl = ServerUrl.Trim(),
                Username = Username.Trim(),
                Password = Password
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Navidrome settings");
        }
    }
}
