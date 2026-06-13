using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.TeamSpeak;

public enum NicknamePlaybackStatus
{
    Idle,
    Playing,
    Paused
}

/// <summary>
/// Updates TeamSpeak client nickname with playback status suffix while connected.
/// </summary>
public sealed class TeamSpeakNicknameService
{
    private readonly ILogger<TeamSpeakNicknameService> _logger;
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly TeamSpeakService _teamSpeak;

    private string _baseNickname = "TS-DJ";
    private NicknamePlaybackStatus _lastAppliedStatus = NicknamePlaybackStatus.Idle;

    public TeamSpeakNicknameService(
        ILogger<TeamSpeakNicknameService> logger,
        ITeamSpeakService teamSpeakService,
        TeamSpeakService teamSpeak)
    {
        _logger = logger;
        _teamSpeakService = teamSpeakService;
        _teamSpeak = teamSpeak;
    }

    public void SetBaseNickname(string nickname)
    {
        _baseNickname = string.IsNullOrWhiteSpace(nickname) ? "TS-DJ" : nickname.Trim();
    }

    public async Task ApplyInitialStatusAsync(CancellationToken cancellationToken = default)
    {
        _lastAppliedStatus = NicknamePlaybackStatus.Idle;
        await ApplyStatusAsync(NicknamePlaybackStatus.Idle, cancellationToken);
    }

    public async Task UpdatePlaybackStatusAsync(PlaybackState state, CancellationToken cancellationToken = default)
    {
        var status = state switch
        {
            PlaybackState.Playing => NicknamePlaybackStatus.Playing,
            PlaybackState.Paused => NicknamePlaybackStatus.Paused,
            _ => NicknamePlaybackStatus.Idle
        };

        await ApplyStatusAsync(status, cancellationToken);
    }

    public async Task RestoreBaseNicknameAsync(CancellationToken cancellationToken = default)
    {
        if (_teamSpeakService.State != ConnectionState.Connected)
            return;

        try
        {
            await _teamSpeakService.SetNicknameAsync(_baseNickname, cancellationToken);
            _lastAppliedStatus = NicknamePlaybackStatus.Idle;
            _logger.LogInformation("Restored base nickname: {Nickname}", _baseNickname);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore base nickname");
        }
    }

    private async Task ApplyStatusAsync(NicknamePlaybackStatus status, CancellationToken cancellationToken)
    {
        if (_teamSpeakService.State != ConnectionState.Connected)
            return;

        if (_lastAppliedStatus == status)
            return;

        var displayName = FormatNickname(_baseNickname, status);

        try
        {
            await _teamSpeakService.SetNicknameAsync(displayName, cancellationToken);
            _lastAppliedStatus = status;
            _logger.LogInformation("Nickname updated to {Nickname}", displayName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update nickname to {Nickname}", displayName);
        }
    }

    internal static string FormatNickname(string baseNickname, NicknamePlaybackStatus status) =>
        $"{baseNickname} | {status}";
}
