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
    private readonly YtDlpDiagnostics _diagnostics;
    private CancellationTokenSource? _saveDebounceCts;
    private bool _isLoading;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private string _jsRuntimePath = string.Empty;

    [ObservableProperty]
    private YoutubeJsRuntimePreference _jsRuntime = YoutubeJsRuntimePreference.Auto;

    [ObservableProperty]
    private bool _enableRemoteEjsComponents = true;

    [ObservableProperty]
    private string _resolvedPathDisplay = "Not resolved";

    [ObservableProperty]
    private string _versionDisplay = "Unknown";

    [ObservableProperty]
    private string _jsRuntimeStatusDisplay = "Not detected";

    [ObservableProperty]
    private string _statusDisplay = "Unknown";

    [ObservableProperty]
    private string _testResult = string.Empty;

    public IReadOnlyList<YoutubeJsRuntimePreference> JsRuntimeOptions { get; } =
        Enum.GetValues<YoutubeJsRuntimePreference>();

    public YtDlpOptionsViewModel(
        ILogger<YtDlpOptionsViewModel> logger,
        ISettingsService settingsService,
        YtDlpLocator ytDlpLocator,
        YtDlpDiagnostics diagnostics)
    {
        _logger = logger;
        _settingsService = settingsService;
        _ytDlpLocator = ytDlpLocator;
        _diagnostics = diagnostics;
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsService.LoadYtDlpSettingsAsync();
            ExecutablePath = settings.ExecutablePath;
            JsRuntimePath = settings.JsRuntimePath;
            JsRuntime = settings.JsRuntime;
            EnableRemoteEjsComponents = settings.EnableRemoteEjsComponents;
            await RefreshDiagnosticsAsync();
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
        _ = RefreshDiagnosticsAsync();
    }

    partial void OnJsRuntimePathChanged(string value)
    {
        ScheduleSave();
        _ = RefreshDiagnosticsAsync();
    }

    partial void OnJsRuntimeChanged(YoutubeJsRuntimePreference value)
    {
        ScheduleSave();
        _ = RefreshDiagnosticsAsync();
    }

    partial void OnEnableRemoteEjsComponentsChanged(bool value) => ScheduleSave();

    [RelayCommand]
    private async Task TestYtDlpAsync()
    {
        TestResult = string.Empty;
        await RefreshDiagnosticsAsync();

        var snapshot = _diagnostics.Current;
        if (snapshot.Status == YoutubeDiagnosticsStatus.NotFound)
        {
            TestResult = "yt-dlp not found.";
            return;
        }

        TestResult = snapshot.Status == YoutubeDiagnosticsStatus.Ready
            ? $"OK — v{snapshot.YtDlpVersion ?? "?"}"
            : snapshot.StatusMessage ?? snapshot.StatusDisplay;
    }

    private async Task RefreshDiagnosticsAsync()
    {
        _ytDlpLocator.InvalidateCache();
        var snapshot = await _diagnostics.RefreshAsync();

        ResolvedPathDisplay = snapshot.YtDlpPath is null
            ? "Not found (configure path or install on PATH)"
            : $"{snapshot.YtDlpOrigin}: {snapshot.YtDlpPath}";

        VersionDisplay = snapshot.YtDlpVersion ?? "Unknown";
        JsRuntimeStatusDisplay = snapshot.JsRuntimeStatus;
        StatusDisplay = snapshot.StatusMessage is null
            ? snapshot.StatusDisplay
            : $"{snapshot.StatusDisplay} — {snapshot.StatusMessage}";
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
                ExecutablePath = ExecutablePath.Trim(),
                JsRuntimePath = JsRuntimePath.Trim(),
                JsRuntime = JsRuntime,
                EnableRemoteEjsComponents = EnableRemoteEjsComponents
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
