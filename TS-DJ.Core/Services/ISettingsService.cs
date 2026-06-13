using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface ISettingsService
{
    Task<ConnectionSettings> LoadConnectionSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveConnectionSettingsAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string? value, CancellationToken cancellationToken = default);
}
