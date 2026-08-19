using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TS_DJ.App.Views;
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
    [NotifyPropertyChangedFor(nameof(ShowCookieFilePath))]
    [NotifyPropertyChangedFor(nameof(ShowCookiesFromBrowser))]
    private YoutubeCookieSource _cookieSource = YoutubeCookieSource.None;

    [ObservableProperty]
    private string _cookieFilePath = string.Empty;

    [ObservableProperty]
    private string _cookiesFromBrowser = string.Empty;

    [ObservableProperty]
    private string _audioFormatSelector = YtDlpSettings.DefaultAudioFormatSelector;

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

    public IReadOnlyList<YoutubeCookieSource> CookieSourceOptions { get; } =
        Enum.GetValues<YoutubeCookieSource>();

    public bool ShowCookieFilePath => CookieSource == YoutubeCookieSource.File;

    public bool ShowCookiesFromBrowser => CookieSource == YoutubeCookieSource.Browser;

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
            CookieSource = settings.CookieSource;
            CookieFilePath = settings.CookieFilePath;
            CookiesFromBrowser = settings.CookiesFromBrowser;
            AudioFormatSelector = string.IsNullOrWhiteSpace(settings.AudioFormatSelector)
                ? YtDlpSettings.DefaultAudioFormatSelector
                : settings.AudioFormatSelector;
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

    partial void OnCookieSourceChanged(YoutubeCookieSource value) => ScheduleSave();

    partial void OnCookieFilePathChanged(string value) => ScheduleSave();

    partial void OnCookiesFromBrowserChanged(string value) => ScheduleSave();

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

    [RelayCommand]
    private async Task BrowseCookieFileAsync()
    {
        var path = await PickCookieFileAsync();
        if (!string.IsNullOrWhiteSpace(path))
            CookieFilePath = path;
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
                EnableRemoteEjsComponents = EnableRemoteEjsComponents,
                AudioFormatSelector = string.IsNullOrWhiteSpace(AudioFormatSelector)
                    ? YtDlpSettings.DefaultAudioFormatSelector
                    : AudioFormatSelector.Trim(),
                CookieSource = CookieSource,
                CookieFilePath = CookieFilePath.Trim(),
                CookiesFromBrowser = CookiesFromBrowser.Trim()
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

    private static async Task<string?> PickCookieFileAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var window = desktop.Windows.OfType<OptionsWindow>().FirstOrDefault()
                     ?? desktop.MainWindow as Window;
        if (window is null)
            return null;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Netscape cookies.txt",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Cookies") { Patterns = ["*.txt"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }
}
