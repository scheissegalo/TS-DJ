using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Playback;

/// <summary>
/// Prefetches decoder-ready media for non-local queue items before transitions.
/// </summary>
public sealed class MediaLoadPrefetchCache
{
    private readonly IMediaPlaybackLoader _loader;
    private readonly ILogger<MediaLoadPrefetchCache>? _logger;
    private readonly ConcurrentDictionary<string, PrefetchEntry> _entries = new(StringComparer.Ordinal);

    public MediaLoadPrefetchCache(
        IMediaPlaybackLoader loader,
        ILogger<MediaLoadPrefetchCache>? logger = null)
    {
        _loader = loader;
        _logger = logger;
    }

    public static bool ShouldPrefetch(PlaybackQueueItem item) =>
        item.SourceKind != PlaybackSourceKind.LocalFile;

    public void StartPrefetch(PlaybackQueueItem item)
    {
        if (!ShouldPrefetch(item))
            return;

        var key = item.SourceKey;
        _entries.AddOrUpdate(
            key,
            _ => new PrefetchEntry(CreateTask(item)),
            (_, existing) =>
            {
                if (!existing.Task.IsCompleted)
                    return existing;

                existing.DisposeResult();
                return new PrefetchEntry(CreateTask(item));
            });
    }

    public void PrefetchNextQueued(IReadOnlyList<PlaybackQueueItem> queue, bool crossfadeEnabled)
    {
        var queued = queue.Where(i => i.Status == PlaybackQueueStatus.Queued).ToList();
        if (queued.Count == 0)
            return;

        StartPrefetch(queued[0]);

        if (crossfadeEnabled && queued.Count > 1)
            StartPrefetch(queued[1]);
    }

    public bool IsReady(PlaybackQueueItem item) =>
        _entries.TryGetValue(item.SourceKey, out var entry)
        && entry.Task.IsCompletedSuccessfully
        && entry.Task.Result is not null;

    public bool IsInProgress(PlaybackQueueItem item) =>
        _entries.TryGetValue(item.SourceKey, out var entry) && !entry.Task.IsCompleted;

    public bool TryTakeReady(PlaybackQueueItem item, out MediaLoadResult? result)
    {
        result = null;
        if (!_entries.TryGetValue(item.SourceKey, out var entry))
            return false;

        if (!entry.Task.IsCompletedSuccessfully || entry.Task.Result is null)
            return false;

        result = entry.Task.Result;
        _entries.TryRemove(item.SourceKey, out _);
        return true;
    }

    public async Task<MediaLoadResult> WaitForReadyAsync(
        PlaybackQueueItem item,
        CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(item.SourceKey, out var entry))
            return await _loader.LoadAsync(item, cancellationToken: cancellationToken);

        MediaLoadResult? result;
        try
        {
            result = await entry.Task.WaitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _entries.TryRemove(item.SourceKey, out _);
            throw new InvalidOperationException($"Prefetch failed for {item.SourceKey}.", ex);
        }

        _entries.TryRemove(item.SourceKey, out _);

        if (result is null)
            throw new InvalidOperationException($"Prefetch returned no media for {item.SourceKey}.");

        return result;
    }

    public void Invalidate(string sourceKey)
    {
        if (_entries.TryRemove(sourceKey, out var entry))
            entry.DisposeResult();
    }

    public void Clear()
    {
        foreach (var key in _entries.Keys.ToList())
            Invalidate(key);
    }

    private Task<MediaLoadResult?> CreateTask(PlaybackQueueItem item) =>
        Task.Run(async () =>
        {
            try
            {
                return await _loader.LoadAsync(item, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Media prefetch failed for {SourceKey}; playback will retry at transition",
                    item.SourceKey);
                return null;
            }
        });

    private sealed class PrefetchEntry : IDisposable
    {
        public PrefetchEntry(Task<MediaLoadResult?> task) => Task = task;

        public Task<MediaLoadResult?> Task { get; }

        public void DisposeResult()
        {
            if (Task.IsCompletedSuccessfully)
                Task.Result?.StreamHandle?.Dispose();
        }

        public void Dispose() => DisposeResult();
    }
}
