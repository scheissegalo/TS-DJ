using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface IAudioMixerService : IDisposable
{
    float MasterVolume { get; set; }

    IMusicTrackSource Music { get; }
    ISoundEffectSource Soundboard { get; }
    IMicrophoneSource Microphone { get; }

    IReadOnlyList<PlaybackQueueItem> Queue { get; }
    PlaybackQueueItem? NowPlaying { get; }

    event EventHandler? QueueChanged;
    event EventHandler? NowPlayingChanged;

    void Enqueue(string filePath);
    void Enqueue(PlaybackQueueItem item);
    void EnqueueRange(IEnumerable<PlaybackQueueItem> items);
    void RemoveFromQueue(int index);
    void ClearQueue();
    void UpdateQueueItemMetadata(string sourceKey, string? displayName = null, string? artist = null, int? durationSeconds = null);
    void PlayQueueItem(int index);
    Task PlayQueueItemAsync(int index, CancellationToken cancellationToken = default);
    void SkipNext();
    void SkipPrevious();
    bool CanSkipPrevious { get; }

    void Start();
    void Pause();
    void Stop();

    void PlaySoundEffect(string filePath, float gainFactor = 1f);

    float SoundboardVolume { get; set; }

    int EncoderBitrateKbps { get; set; }

    event EventHandler? EncoderBitrateChanged;
}
