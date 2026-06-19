using TS_DJ.Core.Models;

namespace TS_DJ.Audio.Playback;

/// <summary>
/// Rolling average load times per source kind for prefetch lookahead and UI estimates.
/// </summary>
public sealed class MediaLoadTimingTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<PlaybackSourceKind, TimingStats> _stats = new();

    public int EstimatedLoadMs(PlaybackSourceKind sourceKind)
    {
        lock (_sync)
        {
            if (_stats.TryGetValue(sourceKind, out var stats) && stats.SampleCount > 0)
                return (int)Math.Round(stats.AverageMs);

            return sourceKind switch
            {
                PlaybackSourceKind.LocalFile => 0,
                PlaybackSourceKind.RemoteStream => 800,
                PlaybackSourceKind.YouTube => 2500,
                _ => 1000
            };
        }
    }

    public void RecordSuccess(PlaybackSourceKind sourceKind, TimeSpan elapsed)
    {
        lock (_sync)
        {
            if (!_stats.TryGetValue(sourceKind, out var stats))
            {
                stats = new TimingStats();
                _stats[sourceKind] = stats;
            }

            stats.Add(elapsed.TotalMilliseconds);
        }
    }

    public void RecordFailure(PlaybackSourceKind sourceKind, TimeSpan elapsed) =>
        RecordSuccess(sourceKind, elapsed);

    private sealed class TimingStats
    {
        private double _totalMs;
        public int SampleCount { get; private set; }
        public double AverageMs => SampleCount == 0 ? 0 : _totalMs / SampleCount;

        public void Add(double elapsedMs)
        {
            _totalMs += elapsedMs;
            SampleCount++;
        }
    }
}
