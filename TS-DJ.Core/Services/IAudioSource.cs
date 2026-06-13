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
    void Play();
    void Stop();

    event EventHandler? TrackEnded;
}

public interface ISoundEffectSource : IAudioSource
{
    void Play(string filePath);
}

public interface IMicrophoneSource : IAudioSource
{
    bool IsCapturing { get; }

    void StartCapture();
    void StopCapture();
}
