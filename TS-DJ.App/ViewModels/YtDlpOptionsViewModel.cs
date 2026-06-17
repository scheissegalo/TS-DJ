using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.YtDlp;

namespace TS_DJ.App.ViewModels;

public partial class YtDlpOptionsViewModel : ViewModelBase
{
    private readonly ILogger<YtDlpOptionsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private readonly YtDlpLocator _ytDlpLocator;
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isLoading;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private string _resolvedPathDisplay = "Not resolved";

    [ObservableProperty]
    private string _testResult = string.Empty;

    public YtDlpOptionsViewModel(
        ILogger<YtDlpOptionsViewModel> logger,
        ISettingsService settingsService,
        YtDlpLocator ytDlpLocator)
    {
        _logger = logger;
        _settingsService = settingsService;
        _ytDlpLocator = ytDlpLocator;
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsService.LoadYtDlpSettingsAsync();
            ExecutablePath = settings.ExecutablePath;
            await RefreshResolvedPathAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load yt-dlp settings");
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnExecutablePathChanged(string value)
    {
        ScheduleSave();
        _ = RefreshResolvedPathAsync();
    }

    [RelayCommand]
    private async Task TestYtDlpAsync()
    {
        TestResult = string.Empty;
        _ytDlpLocator.InvalidateCache();
        var location = await _ytDlpLocator.LocateAsync();
        if (location is null)
        {
            TestResult = "yt-dlp not found.";
            return;
        }

        TestResult = $"OK — {location.Origin}: {location.Path}";
    }

    private async Task RefreshResolvedPathAsync()
    {
        _ytDlpLocator.InvalidateCache();
        var location = await _ytDlpLocator.LocateAsync();
        ResolvedPathDisplay = location is null
            ? "Not found (configure path or install on PATH)"
            : $"{location.Origin}: {location.Path}";
    }

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
            await _settingsService.SaveYtDlpSettingsAsync(new YtDlpSettings
            {
                ExecutablePath = ExecutablePath.Trim()
            }, cancellationToken);
            _ytDlpLocator.InvalidateCache();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save yt-dlp settings");
        }
    }
}
