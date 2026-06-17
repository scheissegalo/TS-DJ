namespace TS_DJ.Core.Services;

/// <summary>
/// Buffered in-memory audio stream ready for the decoder (e.g. yt-dlp MP3 stdout).
/// </summary>
public interface IPlaybackStreamHandle : IDisposable
{
    Stream OpenRead();
}
