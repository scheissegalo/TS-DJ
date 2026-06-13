using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface ISettingsService
{
    Task<ConnectionSettings> LoadConnectionSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveConnectionSettingsAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);

    Task<AudioSettings> LoadAudioSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveAudioSettingsAsync(AudioSettings settings, CancellationToken cancellationToken = default);

    Task<SoundboardSettings> LoadSoundboardSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSoundboardSettingsAsync(SoundboardSettings settings, CancellationToken cancellationToken = default);

    Task<UiSettings> LoadUiSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveUiSettingsAsync(UiSettings settings, CancellationToken cancellationToken = default);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string? value, CancellationToken cancellationToken = default);
}
