using Microsoft.Extensions.Logging;
using TS_DJ.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.YtDlp;

namespace TS_DJ.App.Services;

public sealed class TeamSpeakCommandService
{
    private const string HelpText =
        "Commands: !stop, !next, !volume 1-100, !yt URL, !ytp URL, !help";

    private const string SequentialModeRequired =
        "That command requires playlist (sequential queue) mode.";

    private readonly ILogger<TeamSpeakCommandService> _logger;
    private readonly ITeamSpeakService _teamSpeakService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly IAudioMixerService _audioMixerService;
    private readonly IYoutubeMediaQueueService _youtubeQueue;
    private readonly IPlaybackTargetService _playbackTarget;
    private readonly ISettingsService _settingsService;
    private volatile bool _commandsEnabled;

    public TeamSpeakCommandService(
        ILogger<TeamSpeakCommandService> logger,
        ITeamSpeakService teamSpeakService,
        IAudioPlaybackService audioPlaybackService,
        IAudioMixerService audioMixerService,
        IYoutubeMediaQueueService youtubeQueue,
        IPlaybackTargetService playbackTarget,
        ISettingsService settingsService)
    {
        _logger = logger;
        _teamSpeakService = teamSpeakService;
        _audioPlaybackService = audioPlaybackService;
        _audioMixerService = audioMixerService;
        _youtubeQueue = youtubeQueue;
        _playbackTarget = playbackTarget;
        _settingsService = settingsService;

        _teamSpeakService.TextMessageReceived += OnTextMessageReceived;
        _ = LoadSettingsAsync();
    }

    public event EventHandler<int>? MusicVolumeChanged;

    public void SetEnabled(bool enabled) => _commandsEnabled = enabled;

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadTeamSpeakCommandSettingsAsync(cancellationToken);
        _commandsEnabled = settings.Enabled;
    }

    private void OnTextMessageReceived(object? sender, TeamSpeakTextMessage message)
    {
        if (!_commandsEnabled || _teamSpeakService.State != ConnectionState.Connected)
            return;

        if (!message.Message.TrimStart().StartsWith(TeamSpeakCommandParser.Prefix))
            return;

        _ = Task.Run(() => HandleMessageAsync(message));
    }

    private async Task HandleMessageAsync(TeamSpeakTextMessage message)
    {
        var command = TeamSpeakCommandParser.Parse(message.Message);
        if (command.Kind == TeamSpeakCommandKind.Unknown)
        {
            await ReplyAsync(message, "Unknown command. Try !help");
            return;
        }

        try
        {
            var response = await ExecuteAsync(command);
            if (!string.IsNullOrEmpty(response))
                await ReplyAsync(message, response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command {Kind} from {User} failed", command.Kind, message.InvokerName);
            await ReplyAsync(message, $"Error: {ex.Message}");
        }
    }

    private async Task<string?> ExecuteAsync(TeamSpeakCommand command)
    {
        switch (command.Kind)
        {
            case TeamSpeakCommandKind.Stop:
                await _audioPlaybackService.StopAsync();
                return "Playback stopped.";

            case TeamSpeakCommandKind.Next:
                if (_playbackTarget.IsDualDeckMode)
                    return SequentialModeRequired;

                await _audioPlaybackService.SkipNextAsync();
                return "Skipped to next track.";

            case TeamSpeakCommandKind.SetVolume:
                return await SetVolumeAsync(command.Volume);

            case TeamSpeakCommandKind.YouTubeVideo:
                if (_playbackTarget.IsDualDeckMode)
                    return SequentialModeRequired;

                return await EnqueueYouTubeVideoAsync(command.Url);

            case TeamSpeakCommandKind.YouTubePlaylist:
                if (_playbackTarget.IsDualDeckMode)
                    return SequentialModeRequired;

                return await EnqueueYouTubePlaylistAsync(command.Url);

            case TeamSpeakCommandKind.Help:
                return HelpText;

            default:
                return null;
        }
    }

    private async Task<string> SetVolumeAsync(int volume)
    {
        _audioPlaybackService.Volume = AudioValues.HumanVolumeToFactor(volume);

        var audioSettings = await _settingsService.LoadAudioSettingsAsync();
        audioSettings.MusicVolumeHuman = volume;
        await _settingsService.SaveAudioSettingsAsync(audioSettings);

        MusicVolumeChanged?.Invoke(this, volume);
        _logger.LogInformation("Music volume set to {Volume} via chat command", volume);
        return $"Volume set to {volume}.";
    }

    private async Task<string> EnqueueYouTubeVideoAsync(string rawUrl)
    {
        if (!YoutubeUrlHelper.TryGetSingleVideoUrl(rawUrl, out var url))
            throw new InvalidOperationException("Invalid YouTube URL.");

        var playImmediately = ShouldPlayImmediately();
        _logger.LogDebug(
            "YouTube !yt command: raw={RawUrl} normalized={Url} playImmediately={PlayImmediately}",
            rawUrl,
            url,
            playImmediately);

        var queueCountBefore = _audioMixerService.Queue.Count;
        await _youtubeQueue.EnqueueUrlAsync(url, playImmediately);

        var added = _audioMixerService.Queue
            .Skip(queueCountBefore)
            .LastOrDefault();

        if (added is not null)
            return playImmediately ? $"Now playing: {added.DisplayName}" : $"Queued: {added.DisplayName}";

        return playImmediately ? "Video playing." : "Video queued.";
    }

    private async Task<string> EnqueueYouTubePlaylistAsync(string rawUrl)
    {
        if (!YoutubeUrlHelper.TryGetPlaylistUrl(rawUrl, out var url))
            throw new InvalidOperationException("Invalid YouTube playlist URL.");

        var playImmediately = ShouldPlayImmediately();
        _logger.LogDebug(
            "YouTube !ytp command: raw={RawUrl} normalized={Url} playImmediately={PlayImmediately}",
            rawUrl,
            url,
            playImmediately);

        var queueCountBefore = _audioMixerService.Queue.Count;
        await _youtubeQueue.EnqueuePlaylistAsync(url, playImmediately, replaceQueue: false);

        var addedCount = _audioMixerService.Queue.Count - queueCountBefore;
        if (addedCount > 0)
            return playImmediately
                ? $"Now playing playlist ({addedCount} tracks queued)."
                : $"Queued {addedCount} tracks.";

        return playImmediately ? "Playlist playing." : "Playlist queued.";
    }

    private bool ShouldPlayImmediately() =>
        _audioPlaybackService.State == PlaybackState.Stopped;

    private Task ReplyAsync(TeamSpeakTextMessage message, string text)
    {
        if (message.Target == TeamSpeakMessageTarget.Private)
            return _teamSpeakService.SendPrivateMessageAsync(message.InvokerClientId, text);

        return _teamSpeakService.SendChannelMessageAsync(text);
    }
}
