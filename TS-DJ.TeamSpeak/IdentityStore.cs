using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.TeamSpeak;

public sealed class IdentityStore
{
    private readonly ISettingsService _settingsService;

    public IdentityStore(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<(string PrivateKey, int Offset)> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadConnectionSettingsAsync(cancellationToken);
        return (settings.IdentityPrivateKey, settings.IdentityOffset);
    }

    public async Task SaveAsync(string privateKey, int offset, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadConnectionSettingsAsync(cancellationToken);
        settings.IdentityPrivateKey = privateKey;
        settings.IdentityOffset = offset;
        await _settingsService.SaveConnectionSettingsAsync(settings, cancellationToken);
    }
}
