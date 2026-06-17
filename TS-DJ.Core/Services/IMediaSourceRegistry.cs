using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface IMediaSourceRegistry
{
    IReadOnlyList<IMediaSource> Sources { get; }

    IMediaSource? FindForInput(string input);

    IMediaSource? FindForItem(PlaybackQueueItem item);
}
