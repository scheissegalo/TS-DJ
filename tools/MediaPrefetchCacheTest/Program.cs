using TS_DJ.Audio.Playback;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

var item = new PlaybackQueueItem
{
    SourceKind = PlaybackSourceKind.RemoteStream,
    RemoteTrackId = "prefetch-test-track",
    DisplayName = "Prefetch Test",
    Status = PlaybackQueueStatus.Queued
};

var loader = new DelayedMockLoader(TimeSpan.FromMilliseconds(250));
var cache = new MediaLoadPrefetchCache(loader);

cache.StartPrefetch(item);

var ready = false;
for (var i = 0; i < 50; i++)
{
    if (cache.TryTakeReady(item, out var result) && result is not null)
    {
        ready = true;
        result.StreamHandle?.Dispose();
        break;
    }

    await Task.Delay(50);
}

if (!ready)
{
    Console.Error.WriteLine("FAIL: prefetch did not become ready within timeout");
    return 1;
}

Console.WriteLine("OK: MediaLoadPrefetchCache delivered ready media");
return 0;

internal sealed class DelayedMockLoader : IMediaPlaybackLoader
{
    private readonly TimeSpan _delay;

    public DelayedMockLoader(TimeSpan delay) => _delay = delay;

    public async Task<MediaLoadResult> LoadAsync(
        PlaybackQueueItem item,
        IPlaybackStreamHandle? prefetchedStream = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(_delay, cancellationToken);
        return MediaLoadResult.FromBufferedStream(new ByteArrayPlaybackStreamHandle(new byte[8192]));
    }
}
