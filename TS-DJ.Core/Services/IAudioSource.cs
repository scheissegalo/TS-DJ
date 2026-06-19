using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface IAudioSource : IDisposable
{
    string Id { get; }
    string Name { get; }
    float Volume { get; set; }
    bool IsActive { get; }
}

public interface IMusicTrackSource : IAudioSource
{
    bool IsPlaying { get; }
    string? CurrentFilePath { get; }
    TimeSpan CurrentTime { get; }
    TimeSpan TotalTime { get; }

    void Open(string filePath);
    void Open(PlaybackQueueItem item, MediaLoadResult loadResult);
    void Play();
    void Pause();
    void Stop();
    void Cue();

    float CrossfadeGain { get; }

    event EventHandler? TrackEnded;
}

public interface IDeckChannel : IMusicTrackSource
{
    DeckId DeckId { get; }
    PlaybackQueueItem? LoadedItem { get; }
}

public interface ISoundEffectSource : IAudioSource
{
    void Play(string filePath, float gainFactor = 1f);
}

public interface IMicrophoneSource : IAudioSource
{
    bool IsCapturing { get; }

    void StartCapture();
    void StopCapture();
}
