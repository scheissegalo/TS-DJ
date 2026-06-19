using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class MemoryPlaybackStreamHandle : IPlaybackStreamHandle
{
    private readonly byte[] _data;
    private bool _disposed;

    public MemoryPlaybackStreamHandle(byte[] data) => _data = data;

    public Stream OpenRead()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new MemoryStream(_data, writable: false);
    }

    public void Dispose() => _disposed = true;
}
