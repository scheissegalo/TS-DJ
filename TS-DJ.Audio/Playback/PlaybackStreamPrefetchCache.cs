using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Playback;

/// <summary>
/// Prefetches buffered playback streams for YouTube queue items.
/// </summary>
public sealed class PlaybackStreamPrefetchCache
{
    private readonly IPlaybackStreamOpener _streamOpener;
    private readonly ILogger<PlaybackStreamPrefetchCache>? _logger;
    private readonly ConcurrentDictionary<string, PrefetchEntry> _entries = new(StringComparer.Ordinal);

    public PlaybackStreamPrefetchCache(
        IPlaybackStreamOpener streamOpener,
        ILogger<PlaybackStreamPrefetchCache>? logger = null)
    {
        _streamOpener = streamOpener;
        _logger = logger;
    }

    public void StartPrefetch(PlaybackQueueItem item)
    {
        if (!_streamOpener.CanOpen(item))
            return;

        var key = item.SourceKey;
        _entries.AddOrUpdate(
            key,
            _ => new PrefetchEntry(CreateTask(item)),
            (_, existing) =>
            {
                existing.DisposeHandle();
                return new PrefetchEntry(CreateTask(item));
            });
    }

    public void PrefetchNextQueued(IReadOnlyList<PlaybackQueueItem> queue)
    {
        var next = queue.FirstOrDefault(i => i.Status == PlaybackQueueStatus.Queued);
        if (next is not null)
            StartPrefetch(next);
    }

    public bool TryTakeReady(PlaybackQueueItem item, out IPlaybackStreamHandle? handle)
    {
        handle = null;
        if (!_entries.TryGetValue(item.SourceKey, out var entry))
            return false;

        if (!entry.Task.IsCompletedSuccessfully)
            return false;

        handle = entry.Task.Result;
        _entries.TryRemove(item.SourceKey, out _);
        return handle is not null;
    }

    public void Invalidate(string sourceKey)
    {
        if (_entries.TryRemove(sourceKey, out var entry))
            entry.DisposeHandle();
    }

    public void Clear()
    {
        foreach (var key in _entries.Keys.ToList())
            Invalidate(key);
    }

    private Task<IPlaybackStreamHandle?> CreateTask(PlaybackQueueItem item) =>
        Task.Run(async () =>
        {
            try
            {
                return await _streamOpener.OpenPlaybackStreamAsync(item, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "YouTube prefetch failed for {VideoUrl}; playback will retry when the track starts",
                    item.VideoUrl);
                return null;
            }
        });

    private sealed class PrefetchEntry : IDisposable
    {
        public PrefetchEntry(Task<IPlaybackStreamHandle?> task) => Task = task;

        public Task<IPlaybackStreamHandle?> Task { get; }

        public void DisposeHandle()
        {
            if (Task.IsCompletedSuccessfully)
                Task.Result?.Dispose();
        }

        public void Dispose() => DisposeHandle();
    }
}
