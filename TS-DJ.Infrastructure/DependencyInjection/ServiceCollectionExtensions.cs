using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.Logging;
using TS_DJ.Infrastructure.Navidrome;
using TS_DJ.Infrastructure.Playlists;
using TS_DJ.Infrastructure.Settings;

namespace TS_DJ.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTsDjInfrastructure(this IServiceCollection services, string? settingsDatabasePath = null)
    {
        var dbPath = settingsDatabasePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TS-DJ",
                "settings.db");

        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<UiLogProvider>();
        services.AddSingleton<ISettingsService>(sp => new SqliteSettingsService(
            dbPath,
            sp.GetRequiredService<ILogger<SqliteSettingsService>>()));

        services.AddHttpClient(NavidromeService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TS-DJ");
        });

        services.AddSingleton<NavidromeService>(sp => new NavidromeService(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(NavidromeService.HttpClientName),
            sp.GetRequiredService<ILogger<NavidromeService>>()));

        services.AddSingleton<INavidromeService>(sp => sp.GetRequiredService<NavidromeService>());
        services.AddSingleton<IPlaybackStreamUrlProvider>(sp => sp.GetRequiredService<NavidromeService>());
        services.AddSingleton<IPlaylistService, PlaylistService>();

        return services;
    }
}
