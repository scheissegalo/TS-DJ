using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.TeamSpeak;

namespace TS_DJ.Audio;

/// <summary>
/// Backward-compatible facade over <see cref="Mixing.AudioMixerService"/>.
/// Orchestrates queue playback state for UI and CLI consumers.
/// </summary>
public sealed class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly ILogger<AudioPlaybackService> _logger;
    private readonly IAudioMixerService _mixer;
    private readonly TeamSpeakService _teamSpeak;
    private readonly TeamSpeakNicknameService _nicknameService;
    private PlaybackState _state = PlaybackState.Stopped;
    private float _volume = AudioValues.HumanVolumeToFactor(50f);

    public AudioPlaybackService(
        ILogger<AudioPlaybackService> logger,
        IAudioMixerService mixer,
        TeamSpeakService teamSpeak,
        TeamSpeakNicknameService nicknameService)
    {
        _logger = logger;
        _mixer = mixer;
        _teamSpeak = teamSpeak;
        _nicknameService = nicknameService;

        _mixer.Music.Volume = _volume;
        _mixer.NowPlayingChanged += OnNowPlayingChanged;
        _mixer.QueueChanged += OnQueueChanged;
        _teamSpeak.StateChanged += OnTeamSpeakStateChanged;
    }

    public PlaybackState State => _state;
    public string? CurrentFilePath => _mixer.NowPlaying?.FilePath;
    public TimeSpan CurrentPosition => _mixer.Music.CurrentTime;
    public TimeSpan TotalDuration => _mixer.Music.TotalTime;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _mixer.Music.Volume = _volume;
            _logger.LogDebug("Music volume set to {Volume:P0}", _volume);
        }
    }

    public event EventHandler<PlaybackState>? StateChanged;

    public async Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _mixer.Enqueue(filePath);
        _logger.LogInformation(
            "Enqueued via LoadAsync: {FilePath} (queue size={QueueSize})",
            filePath, _mixer.Queue.Count);

        if (!_teamSpeak.Client.Connected)
            return;

        if (_mixer.Music.IsPlaying)
        {
            _logger.LogInformation(
                "LoadAsync: mixer already playing — {FilePath} queued for auto-advance",
                filePath);
            return;
        }

        if (!_mixer.Queue.Any(i => i.Status == PlaybackQueueStatus.Queued))
            return;

        await PlayAsync(cancellationToken);
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        var hasQueued = _mixer.Queue.Any(item => item.Status == PlaybackQueueStatus.Queued);
        if (!hasQueued && !_mixer.Music.IsPlaying)
        {
            _logger.LogWarning("Play requested but queue is empty");
            return Task.CompletedTask;
        }

        if (!_teamSpeak.Client.Connected)
        {
            _logger.LogWarning("Cannot play — not connected to TeamSpeak");
            return Task.CompletedTask;
        }

        try
        {
            _logger.LogInformation("PlayAsync requested (state={State}, music.IsPlaying={IsPlaying})",
                _state, _mixer.Music.IsPlaying);

            _mixer.Start();

            if (_mixer.Music.IsPlaying)
            {
                SetState(PlaybackState.Playing);
                _logger.LogInformation("Playback started: {FilePath}", CurrentFilePath);
            }
            else if (!_mixer.Queue.Any(i => i.Status == PlaybackQueueStatus.Queued))
            {
                _logger.LogWarning("Play requested but no playable queued tracks remain");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start playback");
            _mixer.Stop();
            SetState(PlaybackState.Stopped);
        }

        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_state != PlaybackState.Playing)
            return Task.CompletedTask;

        _mixer.Pause();
        SetState(PlaybackState.Paused);
        _logger.LogInformation("Playback paused");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        SetState(PlaybackState.Stopped);
        _mixer.Stop();
        _logger.LogInformation("Playback stopped");
        return Task.CompletedTask;
    }

    public Task PlayQueueItemAsync(int index, CancellationToken cancellationToken = default)
    {
        if (!_teamSpeak.Client.Connected)
        {
            _logger.LogWarning("Cannot play queue item — not connected to TeamSpeak");
            return Task.CompletedTask;
        }

        _mixer.PlayQueueItem(index);

        if (_mixer.Music.IsPlaying && _mixer.NowPlaying is not null)
        {
            SetState(PlaybackState.Playing);
            _logger.LogInformation("PlayQueueItem started: {FilePath}", _mixer.NowPlaying.FilePath);
        }

        return Task.CompletedTask;
    }

    public Task SkipNextAsync(CancellationToken cancellationToken = default)
    {
        if (!_teamSpeak.Client.Connected)
            return Task.CompletedTask;

        _mixer.SkipNext();

        if (_mixer.Music.IsPlaying && _mixer.NowPlaying is not null)
            SetState(PlaybackState.Playing);
        else if (_state == PlaybackState.Playing)
            SetState(PlaybackState.Stopped);

        return Task.CompletedTask;
    }

    public Task SkipPreviousAsync(CancellationToken cancellationToken = default)
    {
        if (!_teamSpeak.Client.Connected)
            return Task.CompletedTask;

        _mixer.SkipPrevious();

        if (_mixer.Music.IsPlaying && _mixer.NowPlaying is not null)
            SetState(PlaybackState.Playing);

        return Task.CompletedTask;
    }

    public void RemoveFromQueue(int index) => _mixer.RemoveFromQueue(index);

    private void OnNowPlayingChanged(object? sender, EventArgs e)
    {
        _logger.LogInformation(
            "NowPlayingChanged: nowPlaying={NowPlaying}, music.IsPlaying={IsPlaying}, state={State}",
            _mixer.NowPlaying?.DisplayName ?? "none",
            _mixer.Music.IsPlaying,
            _state);

        SyncPlaybackState("NowPlayingChanged");
        TryContinueQueue("NowPlayingChanged");
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        var queued = _mixer.Queue.Count(i => i.Status == PlaybackQueueStatus.Queued);
        var playing = _mixer.Queue.Count(i => i.Status == PlaybackQueueStatus.Playing);
        var played = _mixer.Queue.Count(i => i.Status == PlaybackQueueStatus.Played);

        _logger.LogInformation(
            "QueueChanged: total={Total}, queued={Queued}, playing={Playing}, played={Played}",
            _mixer.Queue.Count, queued, playing, played);

        SyncPlaybackState("QueueChanged");
        TryContinueQueue("QueueChanged");
    }

    private void SyncPlaybackState(string reason)
    {
        if (_state == PlaybackState.Paused)
            return;

        var isActivelyPlaying = _mixer.Music.IsPlaying && _mixer.NowPlaying is not null;
        var hasQueued = _mixer.Queue.Any(i => i.Status == PlaybackQueueStatus.Queued);
        var hasWaitingTrack = hasQueued
            || _mixer.Queue.Any(i => i.Status == PlaybackQueueStatus.Playing);

        if (isActivelyPlaying)
        {
            SetState(PlaybackState.Playing);
            _logger.LogDebug("SyncPlaybackState ({Reason}): Playing {FilePath}", reason, _mixer.NowPlaying!.FilePath);
            return;
        }

        if (_state == PlaybackState.Playing && hasWaitingTrack && _teamSpeak.Client.Connected)
        {
            _logger.LogInformation(
                "SyncPlaybackState ({Reason}): facade Playing but mixer idle — calling Start()",
                reason);
            _mixer.Start();

            if (_mixer.Music.IsPlaying && _mixer.NowPlaying is not null)
            {
                SetState(PlaybackState.Playing);
                return;
            }
        }

        if (hasQueued)
        {
            _logger.LogDebug(
                "SyncPlaybackState ({Reason}): idle with {Queued} queued — holding state {State}",
                reason, _mixer.Queue.Count(i => i.Status == PlaybackQueueStatus.Queued), _state);
            return;
        }

        if (_state == PlaybackState.Playing || _state == PlaybackState.Paused)
        {
            SetState(PlaybackState.Stopped);
            _logger.LogInformation("SyncPlaybackState ({Reason}): queue empty/idle — Stopped", reason);
        }
    }

    private void TryContinueQueue(string reason)
    {
        if (_state != PlaybackState.Playing)
            return;

        if (!_teamSpeak.Client.Connected)
            return;

        var hasQueued = _mixer.Queue.Any(i => i.Status == PlaybackQueueStatus.Queued);
        var isActivelyPlaying = _mixer.Music.IsPlaying && _mixer.NowPlaying is not null;

        if (!hasQueued || isActivelyPlaying)
            return;

        _logger.LogInformation(
            "TryContinueQueue ({Reason}): {Queued} track(s) queued but nothing playing — calling Start()",
            reason, _mixer.Queue.Count(i => i.Status == PlaybackQueueStatus.Queued));

        _mixer.Start();

        if (_mixer.Music.IsPlaying && _mixer.NowPlaying is not null)
        {
            SetState(PlaybackState.Playing);
            _logger.LogInformation(
                "TryContinueQueue ({Reason}): advanced to {FilePath}",
                reason, _mixer.NowPlaying.FilePath);
        }
        else
        {
            _logger.LogWarning(
                "TryContinueQueue ({Reason}): Start() did not begin playback",
                reason);
        }
    }

    private void OnTeamSpeakStateChanged(object? sender, ConnectionState state)
    {
        if (state is ConnectionState.Disconnected or ConnectionState.Error)
            _ = StopAsync();
    }

    private void SetState(PlaybackState state)
    {
        if (_state == state)
            return;

        _logger.LogDebug("Playback state transition: {OldState} → {NewState}", _state, state);
        _state = state;
        StateChanged?.Invoke(this, state);
        _ = _nicknameService.UpdatePlaybackStatusAsync(state);
    }

    public void Dispose()
    {
        _mixer.Stop();
        _mixer.NowPlayingChanged -= OnNowPlayingChanged;
        _mixer.QueueChanged -= OnQueueChanged;
        _teamSpeak.StateChanged -= OnTeamSpeakStateChanged;
    }
}
