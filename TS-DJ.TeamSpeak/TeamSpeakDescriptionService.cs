using Microsoft.Extensions.Logging;
using System.Diagnostics;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.TeamSpeak;

/// <summary>
/// Updates TeamSpeak client description with now-playing metadata on track changes.
/// </summary>
public sealed class TeamSpeakDescriptionService
{
    private readonly ILogger<TeamSpeakDescriptionService> _logger;
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly IAudioMixerService _mixer;
    private readonly ITrackTransitionTiming? _transitionTiming;
    private string? _lastAppliedSourceKey;

    public TeamSpeakDescriptionService(
        ILogger<TeamSpeakDescriptionService> logger,
        ITeamSpeakService teamSpeakService,
        IAudioMixerService mixer,
        ITrackTransitionTiming? transitionTiming = null)
    {
        _logger = logger;
        _teamSpeakService = teamSpeakService;
        _mixer = mixer;
        _transitionTiming = transitionTiming;

        _mixer.NowPlayingChanged += OnNowPlayingChanged;
        _teamSpeakService.StateChanged += OnConnectionStateChanged;
    }

    public async Task ClearDescriptionAsync(CancellationToken cancellationToken = default)
    {
        _lastAppliedSourceKey = null;
        await ApplyDescriptionAsync(string.Empty, sourceKey: null, cancellationToken);
    }

    private void OnNowPlayingChanged(object? sender, EventArgs e)
    {
        _ = UpdateFromNowPlayingAsync();
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        if (state == ConnectionState.Connected)
        {
            _ = UpdateFromNowPlayingAsync();
            return;
        }

        if (state is ConnectionState.Disconnected or ConnectionState.Error)
        {
            _lastAppliedSourceKey = null;
            _ = ApplyDescriptionAsync(string.Empty, sourceKey: null);
        }
    }

    private async Task UpdateFromNowPlayingAsync(CancellationToken cancellationToken = default)
    {
        var nowPlaying = _mixer.NowPlaying;

        if (nowPlaying is null)
        {
            if (_lastAppliedSourceKey is null)
            {
                _transitionTiming?.MarkTsDescriptionSkipped();
                return;
            }

            _lastAppliedSourceKey = null;
            await ApplyDescriptionAsync(string.Empty, sourceKey: null, cancellationToken);
            return;
        }

        if (_lastAppliedSourceKey == nowPlaying.SourceKey)
        {
            _transitionTiming?.MarkTsDescriptionSkipped();
            return;
        }

        string description;
        try
        {
            description = TeamSpeakDescriptionFormatter.Format(nowPlaying, _mixer.EncoderBitrateKbps);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to format TS description for {SourceKey}", nowPlaying.SourceKey);
            description = $"Now Playing:\n{nowPlaying.DisplayName}";
            if (description.Length > TeamSpeakDescriptionFormatter.MaxDescriptionLength)
                description = description[..TeamSpeakDescriptionFormatter.MaxDescriptionLength];
        }

        await ApplyDescriptionAsync(description, nowPlaying.SourceKey, cancellationToken);
    }

    private async Task ApplyDescriptionAsync(
        string description,
        string? sourceKey,
        CancellationToken cancellationToken = default)
    {
        if (_teamSpeakService.State != ConnectionState.Connected)
        {
            _transitionTiming?.MarkTsDescriptionSkipped();
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            await _teamSpeakService.SetDescriptionAsync(description, cancellationToken);
            _lastAppliedSourceKey = sourceKey;

            if (string.IsNullOrEmpty(description))
                _logger.LogInformation("TeamSpeak description cleared");
            else
                _logger.LogInformation(
                    "TeamSpeak description updated for {SourceKey} ({Length} chars)",
                    sourceKey, description.Length);

            _transitionTiming?.MarkTsDescriptionComplete(sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update TeamSpeak description");
            _transitionTiming?.MarkTsDescriptionComplete(sw.Elapsed);
        }
    }
}
