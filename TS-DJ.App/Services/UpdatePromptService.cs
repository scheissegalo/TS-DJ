using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.App.ViewModels;
using TS_DJ.App.Views;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.Services;

public sealed class UpdatePromptService
{
    private readonly IUpdateService _updateService;
    private readonly ILogger<UpdatePromptService> _logger;
    private int _isPromptVisible;

    public UpdatePromptService(IUpdateService updateService, ILogger<UpdatePromptService> logger)
    {
        _updateService = updateService;
        _logger = logger;
    }

    public async Task CheckOnStartupAsync(CancellationToken cancellationToken = default)
    {
        if (!_updateService.IsSupportedEnvironment)
            return;

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(cancellationToken);
            if (!result.HasUpdate || result.AvailableUpdate is null)
                return;

            await Dispatcher.UIThread.InvokeAsync(async () =>
                await ShowUpdateDialogAsync(result.AvailableUpdate, result.CurrentVersion));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup update check failed");
        }
    }

    public async Task<string?> CheckManuallyAsync(CancellationToken cancellationToken = default)
    {
        if (!_updateService.IsSupportedEnvironment)
            return "Updates are only available in release installs that include TS-DJ.Updater.";

        var result = await _updateService.CheckForUpdatesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            return $"Update check failed: {result.ErrorMessage}";

        if (result.AvailableUpdate is null)
            return "Could not read the latest release from GitHub.";

        if (!result.HasUpdate)
            return $"You are up to date (TS-DJ {result.CurrentVersion}).";

        await Dispatcher.UIThread.InvokeAsync(async () =>
            await ShowUpdateDialogAsync(result.AvailableUpdate, result.CurrentVersion));

        return null;
    }

    private async Task ShowUpdateDialogAsync(UpdateReleaseInfo release, Version currentVersion)
    {
        if (Interlocked.CompareExchange(ref _isPromptVisible, 1, 0) != 0)
            return;

        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            if (desktop.MainWindow is not Window owner)
                return;

            var viewModel = App.Services.GetRequiredService<UpdateAvailableDialogViewModel>();
            viewModel.Initialize(release, currentVersion);

            var dialog = new UpdateAvailableDialog
            {
                DataContext = viewModel
            };

            await dialog.ShowDialog<bool?>(owner);
        }
        finally
        {
            Interlocked.Exchange(ref _isPromptVisible, 0);
        }
    }
}
