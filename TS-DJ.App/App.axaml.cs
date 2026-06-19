using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TS_DJ.App.Services;
using TS_DJ.App.ViewModels;
using TS_DJ.App.Views;
using TS_DJ.Core.Services;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.Infrastructure.Logging;
using TS_DJ.Infrastructure.YtDlp;
using TS_DJ.TeamSpeak;
using TS_DJ.TeamSpeak.DependencyInjection;

namespace TS_DJ.App;

public partial class App : Application
{
    private IHost? _host;

    public static IServiceProvider Services =>
        ((App)Current!)._host!.Services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddTsDjInfrastructure();
                services.AddTsDjTeamSpeak();
                services.AddTsDjAudio();
                services.AddSingleton<NavidromeMediaQueueService>();
                services.AddSingleton<INavidromeMediaQueueService>(sp => sp.GetRequiredService<NavidromeMediaQueueService>());
                services.AddSingleton<YoutubeMediaQueueService>();
                services.AddSingleton<IYoutubeMediaQueueService>(sp => sp.GetRequiredService<YoutubeMediaQueueService>());
                services.AddSingleton<YoutubePlaylistEnrichmentService>();
                services.AddTransient<NavidromeBrowserViewModel>();
                services.AddTransient<YouTubeUrlDialogViewModel>();
                services.AddSingleton<ConnectionProfileSelectorViewModel>();
                services.AddSingleton<SoundboardViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<ApplicationShutdownService>();

                services.AddTransient<OptionsViewModel>();
                services.AddTransient<TeamSpeakConnectionsOptionsViewModel>();
                services.AddTransient<AudioOptionsViewModel>();
                services.AddTransient<NavidromeOptionsViewModel>();
                services.AddTransient<YtDlpOptionsViewModel>();
                services.AddTransient<SoundboardOptionsViewModel>();
                services.AddTransient<UiPreferencesOptionsViewModel>();
                services.AddTransient<PlaylistManagerViewModel>();
            })
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
                // Avoid logging Navidrome stream/auth URLs from HttpClient
                logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            })
            .Build();

        var logService = _host.Services.GetRequiredService<Core.Services.ILogService>();
        _host.Services.GetRequiredService<ILoggerFactory>().AddProvider(new UiLogProvider(logService));
        _ = _host.Services.GetRequiredService<TeamSpeakDescriptionService>();

        var appLogger = _host.Services.GetRequiredService<ILogger<App>>();
        if (TSLib.Audio.Opus.NativeMethods.PreloadLibrary())
            appLogger.LogInformation("libopus loaded successfully");
        else
            appLogger.LogError("Failed to load libopus — Opus encoding will not work");

        appLogger.LogInformation("TS-DJ started");

        _ = Task.Run(async () =>
        {
            try
            {
                var diagnostics = _host.Services.GetRequiredService<YtDlpDiagnostics>();
                await diagnostics.LogStartupAsync();
            }
            catch (Exception ex)
            {
                appLogger.LogWarning(ex, "YouTube startup diagnostics failed");
            }
        });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _host.Services.GetRequiredService<MainWindowViewModel>()
            };

            desktop.Exit += (_, _) =>
            {
                if (_host.Services.GetService<MainWindowViewModel>() is IDisposable disposable)
                    disposable.Dispose();

                _host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
