using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Media;

public sealed class CompositePlaybackStreamResolver : IPlaybackStreamResolver
{
    private readonly IEnumerable<IMediaSource> _sources;

    public CompositePlaybackStreamResolver(IEnumerable<IMediaSource> sources)
    {
        _sources = sources;
    }

    public async Task<string?> ResolveStreamUrlAsync(
        PlaybackQueueItem item,
        CancellationToken cancellationToken = default)
    {
        foreach (var source in _sources)
        {
            if (!source.CanHandleItem(item))
                continue;

            var url = await source.ResolveStreamUrlAsync(item, cancellationToken);
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }
}
