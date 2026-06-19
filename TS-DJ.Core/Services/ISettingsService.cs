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

    Task<NavidromeSettings> LoadNavidromeSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveNavidromeSettingsAsync(NavidromeSettings settings, CancellationToken cancellationToken = default);

    Task<YtDlpSettings> LoadYtDlpSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveYtDlpSettingsAsync(YtDlpSettings settings, CancellationToken cancellationToken = default);

    Task<PlaybackSettings> LoadPlaybackSettingsAsync(CancellationToken cancellationToken = default);
    Task SavePlaybackSettingsAsync(PlaybackSettings settings, CancellationToken cancellationToken = default);

    Task<TeamSpeakConnectionProfilesSettings> LoadTeamSpeakConnectionProfilesAsync(CancellationToken cancellationToken = default);
    Task SaveTeamSpeakConnectionProfilesAsync(TeamSpeakConnectionProfilesSettings settings, CancellationToken cancellationToken = default);

    Task<PlaylistLibrary> LoadPlaylistLibraryAsync(CancellationToken cancellationToken = default);
    Task SavePlaylistLibraryAsync(PlaylistLibrary library, CancellationToken cancellationToken = default);
    Task<SavedPlaylist?> LoadSavedPlaylistAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveSavedPlaylistAsync(SavedPlaylist playlist, CancellationToken cancellationToken = default);
    Task DeleteSavedPlaylistAsync(Guid id, CancellationToken cancellationToken = default);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string? value, CancellationToken cancellationToken = default);
}
