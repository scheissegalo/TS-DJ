using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.TeamSpeak;
using TSLib.Helper;

namespace TS_DJ.Audio;

public sealed class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly ILogger<AudioPlaybackService> _logger;
    private readonly ILogger<NaudioFileProducer> _producerLogger;
    private readonly TeamSpeakService _teamSpeak;
    private readonly AudioPipeline _pipeline;
    private NaudioFileProducer? _producer;
    private PlaybackState _state = PlaybackState.Stopped;
    private string? _currentFilePath;
    private float _volume = AudioValues.HumanVolumeToFactor(50f);

    public AudioPlaybackService(
        ILogger<AudioPlaybackService> logger,
        ILogger<NaudioFileProducer> producerLogger,
        TeamSpeakService teamSpeak)
    {
        _logger = logger;
        _producerLogger = producerLogger;
        _teamSpeak = teamSpeak;
        _pipeline = new AudioPipeline(new Id(2));
        _pipeline.SetTarget(teamSpeak.VoiceTarget);
        _pipeline.SetVolume(50f);

        _teamSpeak.StateChanged += OnTeamSpeakStateChanged;
    }

    public PlaybackState State => _state;
    public string? CurrentFilePath => _currentFilePath;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            _pipeline.SetVolume(AudioValues.FactorToHumanVolume(_volume));
            _logger.LogDebug("Volume set to {Volume:P0}", _volume);
        }
    }

    public event EventHandler<PlaybackState>? StateChanged;

    public Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        _currentFilePath = filePath;
        SetState(PlaybackState.Stopped);
        _logger.LogInformation("Loaded audio file: {FilePath}", filePath);
        return Task.CompletedTask;
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning("Play requested but no file is loaded");
            return Task.CompletedTask;
        }

        if (!_teamSpeak.Client.Connected)
        {
            _logger.LogWarning("Cannot play — not connected to TeamSpeak");
            return Task.CompletedTask;
        }

        try
        {
            StopInternal();

            _producer = new NaudioFileProducer(_producerLogger);
            _producer.SongEnded += OnSongEnded;
            _producer.Open(_currentFilePath);

            _pipeline.SetSource(_producer);
            _pipeline.ResetTimer();
            _pipeline.Start();

            SetState(PlaybackState.Playing);
            _logger.LogInformation("Playback started: {FilePath}", _currentFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start playback for {FilePath}", _currentFilePath);
            StopInternal();
            SetState(PlaybackState.Stopped);
        }

        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_state != PlaybackState.Playing)
            return Task.CompletedTask;

        _pipeline.Pause();
        SetState(PlaybackState.Paused);
        _logger.LogInformation("Playback paused");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopInternal();
        SetState(PlaybackState.Stopped);
        _logger.LogInformation("Playback stopped");
        return Task.CompletedTask;
    }

    private void StopInternal()
    {
        _pipeline.Pause();
        if (_producer != null)
        {
            _producer.SongEnded -= OnSongEnded;
            _pipeline.ClearSource();
            _producer = null;
        }
    }

    private void OnSongEnded(object? sender, EventArgs e)
    {
        _logger.LogInformation("Playback finished (end of file)");
        StopInternal();
        SetState(PlaybackState.Stopped);
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

        _state = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _teamSpeak.StateChanged -= OnTeamSpeakStateChanged;
        StopInternal();
        _pipeline.Dispose();
    }
}
