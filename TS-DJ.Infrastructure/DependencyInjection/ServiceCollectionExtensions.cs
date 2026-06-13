using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.Logging;
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

        return services;
    }
}
