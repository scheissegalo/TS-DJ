using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Services;

namespace TS_DJ.App.Services;

/// <summary>
/// Collects per-track-transition timing and logs a single summary line when UI and TS work complete.
/// </summary>
public sealed class TrackTransitionProfiler : ITrackTransitionTiming
{
    private readonly ILogger<TrackTransitionProfiler> _logger;
    private Stopwatch? _totalWatch;
    private string? _sourceKey;
    private long _queueSyncMs;
    private long _autoScrollMs;
    private long _tsDescriptionMs;
    private bool _uiComplete;
    private bool _tsComplete;

    public TrackTransitionProfiler(ILogger<TrackTransitionProfiler> logger)
    {
        _logger = logger;
    }

    public void BeginTransition(string? sourceKey)
    {
        if (string.IsNullOrEmpty(sourceKey))
            return;

        _sourceKey = sourceKey;
        _totalWatch = Stopwatch.StartNew();
        _queueSyncMs = 0;
        _autoScrollMs = 0;
        _tsDescriptionMs = 0;
        _uiComplete = false;
        _tsComplete = false;
    }

    public void RecordQueueSync(TimeSpan elapsed) =>
        _queueSyncMs = (long)elapsed.TotalMilliseconds;

    public void MarkUiComplete(TimeSpan autoScrollElapsed)
    {
        if (_totalWatch is null)
            return;

        _autoScrollMs = (long)autoScrollElapsed.TotalMilliseconds;
        _uiComplete = true;
        TryLog();
    }

    public void MarkTsDescriptionComplete(TimeSpan elapsed)
    {
        if (_totalWatch is null)
            return;

        _tsDescriptionMs = (long)elapsed.TotalMilliseconds;
        _tsComplete = true;
        TryLog();
    }

    public void MarkTsDescriptionSkipped()
    {
        if (_totalWatch is null)
            return;

        _tsDescriptionMs = 0;
        _tsComplete = true;
        TryLog();
    }

    private void TryLog()
    {
        if (!_uiComplete || !_tsComplete || _totalWatch is null || _sourceKey is null)
            return;

        var totalMs = (long)_totalWatch.Elapsed.TotalMilliseconds;
        _logger.LogInformation(
            "Track transition ({SourceKey}): Queue update: {QueueMs}ms, Auto-scroll: {ScrollMs}ms, TS description: {TsMs}ms, Total: {TotalMs}ms",
            _sourceKey,
            _queueSyncMs,
            _autoScrollMs,
            _tsDescriptionMs,
            totalMs);

        Reset();
    }

    private void Reset()
    {
        _totalWatch = null;
        _sourceKey = null;
        _uiComplete = false;
        _tsComplete = false;
    }
}
