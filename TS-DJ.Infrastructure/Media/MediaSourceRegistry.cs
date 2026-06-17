using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Media;

public sealed class MediaSourceRegistry : IMediaSourceRegistry
{
    private readonly IReadOnlyList<IMediaSource> _sources;

    public MediaSourceRegistry(IEnumerable<IMediaSource> sources)
    {
        _sources = sources.ToList();
        Sources = _sources;
    }

    public IReadOnlyList<IMediaSource> Sources { get; }

    public IMediaSource? FindForInput(string input) =>
        _sources.FirstOrDefault(s => s.CanHandleInput(input));

    public IMediaSource? FindForItem(PlaybackQueueItem item) =>
        _sources.FirstOrDefault(s => s.CanHandleItem(item));
}
