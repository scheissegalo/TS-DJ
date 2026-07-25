using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS_DJ.App.Services;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class UpdateAvailableDialogViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;
    private readonly ApplicationShutdownService _shutdownService;
    private UpdateReleaseInfo? _release;

    [ObservableProperty]
    private string _currentVersionText = string.Empty;

    [ObservableProperty]
    private string _newVersionText = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private string _releasePageUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private bool _hasDownloadProgress;

    public bool HasReleasePage => !string.IsNullOrWhiteSpace(ReleasePageUrl);

    public UpdateAvailableDialogViewModel(
        IUpdateService updateService,
        ApplicationShutdownService shutdownService)
    {
        _updateService = updateService;
        _shutdownService = shutdownService;
    }

    public void Initialize(UpdateReleaseInfo release, Version currentVersion)
    {
        _release = release;
        CurrentVersionText = currentVersion.ToString();
        NewVersionText = release.Version.ToString();
        ReleaseNotes = string.IsNullOrWhiteSpace(release.ReleaseNotes)
            ? "No release notes were provided."
            : release.ReleaseNotes;
        ReleasePageUrl = release.ReleasePageUrl;
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (string.IsNullOrWhiteSpace(ReleasePageUrl))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = ReleasePageUrl,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_release is null || IsBusy)
            return;

        IsBusy = true;
        HasDownloadProgress = false;
        StatusMessage = "Downloading update...";

        try
        {
            var progress = new Progress<double>(value =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    DownloadProgress = value * 100;
                    HasDownloadProgress = true;
                });
            });

            var packagePath = await _updateService.DownloadUpdateAsync(_release, progress);
            StatusMessage = "Installing update and restarting...";

            if (!await _updateService.ApplyUpdateAsync(packagePath))
            {
                StatusMessage = "Could not launch the updater.";
                IsBusy = false;
                return;
            }

            await _shutdownService.ShutdownAsync();

            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is Avalonia.Controls.Window mainWindow)
            {
                await Dispatcher.UIThread.InvokeAsync(mainWindow.Close);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
            IsBusy = false;
            HasDownloadProgress = false;
        }
    }
}
