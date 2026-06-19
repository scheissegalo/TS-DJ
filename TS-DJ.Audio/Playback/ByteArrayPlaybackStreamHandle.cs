using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Playback;

/// <summary>
/// In-memory playback handle for prefetched remote or YouTube audio.
/// </summary>
public sealed class ByteArrayPlaybackStreamHandle : IPlaybackStreamHandle
{
    private readonly byte[] _data;
    private bool _disposed;

    public ByteArrayPlaybackStreamHandle(byte[] data) => _data = data;

    public Stream OpenRead()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new MemoryStream(_data, writable: false);
    }

    public void Dispose() => _disposed = true;
}
