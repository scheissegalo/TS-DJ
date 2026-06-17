using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Media;

public sealed class CompositePlaybackStreamOpener : IPlaybackStreamOpener
{
    private readonly IEnumerable<IPlaybackStreamOpener> _openers;

    public CompositePlaybackStreamOpener(IEnumerable<IPlaybackStreamOpener> openers)
    {
        _openers = openers;
    }

    public bool CanOpen(PlaybackQueueItem item) =>
        _openers.Any(o => o.CanOpen(item));

    public async Task<IPlaybackStreamHandle?> OpenPlaybackStreamAsync(
        PlaybackQueueItem item,
        CancellationToken cancellationToken = default)
    {
        foreach (var opener in _openers)
        {
            if (!opener.CanOpen(item))
                continue;

            var handle = await opener.OpenPlaybackStreamAsync(item, cancellationToken);
            if (handle is not null)
                return handle;
        }

        return null;
    }
}
