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
    void RemoveFromQueue(int index);
    void ClearQueue();
    void PlayQueueItem(int index);
    void SkipNext();
    void SkipPrevious();
    bool CanSkipPrevious { get; }

    void Start();
    void Pause();
    void Stop();

    int EncoderBitrateKbps { get; set; }

    event EventHandler? EncoderBitrateChanged;
}
