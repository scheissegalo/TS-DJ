using Microsoft.Extensions.Logging;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Mixing.Sources;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Deck wrapper over <see cref="MusicTrackSource"/> with loaded-item tracking.
/// </summary>
public sealed class DeckChannel : IDeckChannel
{
    private readonly MusicTrackSource _source;
    private PlaybackQueueItem? _loadedItem;

    public DeckChannel(
        DeckId deckId,
        MixingSampleProvider mixer,
        object sync,
        ILogger<MusicTrackSource> logger)
    {
        DeckId = deckId;
        var id = deckId == DeckId.A ? "deck-a" : "deck-b";
        var name = deckId == DeckId.A ? "Deck A" : "Deck B";
        _source = new MusicTrackSource(id, name, mixer, sync, logger);
    }

    internal MusicTrackSource Source => _source;

    public DeckId DeckId { get; }
    public PlaybackQueueItem? LoadedItem => _loadedItem;

    public string Id => _source.Id;
    public string Name => _source.Name;
    public bool IsActive => _source.IsActive;
    public bool IsPlaying => _source.IsPlaying;
    public string? CurrentFilePath => _source.CurrentFilePath;
    public TimeSpan CurrentTime => _source.CurrentTime;
    public TimeSpan TotalTime => _source.TotalTime;

    public float Volume
    {
        get => _source.Volume;
        set => _source.Volume = value;
    }

    public event EventHandler? TrackEnded
    {
        add => _source.TrackEnded += value;
        remove => _source.TrackEnded -= value;
    }

    public void Open(string filePath)
    {
        var item = PlaybackQueueItem.FromLocalFile(filePath);
        Open(item, MediaLoadResult.FromLocalFile(filePath));
    }

    public void Open(PlaybackQueueItem item, MediaLoadResult loadResult)
    {
        _loadedItem = CloneItem(item);
        _source.Open(item, loadResult);
    }

    public void Play() => _source.Play();
    public void Pause() => _source.Pause();
    public void Stop() => _source.Stop();

    public void Cue()
    {
        _source.Cue();
        _loadedItem = null;
    }

    public float CrossfadeGain => _source.CrossfadeGain;

    internal void ResetCrossfadeGain() => _source.ResetCrossfadeGain();
    internal void SetCrossfadeGain(float gain) => _source.SetCrossfadeGain(gain);
    internal bool IsOutputting => _source.IsOutputting;
    internal bool HasActiveTrack => _source.HasActiveTrack;

    internal void PauseOutput() => _source.PauseOutput();
    internal void ResumeOutput() => _source.ResumeOutput();

    internal bool TryConsumeTrackEnd(out string? finishedPath) =>
        _source.TryConsumeTrackEnd(out finishedPath);

    internal bool TryConsumeTrackDecodeFailure(out string? failedSourceKey, out Exception? error) =>
        _source.TryConsumeTrackDecodeFailure(out failedSourceKey, out error);

    internal void AbortTrackDueToDecodeError(Exception ex) =>
        _source.AbortTrackDueToDecodeError(ex);

    internal void ForceTrackEnd() => _source.ForceTrackEnd();

    internal void NotifyMixedRead(int sampleBytes) =>
        _source.NotifyMixedRead(sampleBytes);

    internal bool IsStalled(int threshold) => _source.IsStalled(threshold);

    internal void NotifyTrackEndedEvent() => _source.NotifyTrackEndedEvent();

    public void Dispose() => _source.Dispose();

    private static PlaybackQueueItem CloneItem(PlaybackQueueItem item) =>
        new()
        {
            SourceKind = item.SourceKind,
            FilePath = item.FilePath,
            RemoteTrackId = item.RemoteTrackId,
            VideoUrl = item.VideoUrl,
            ThumbnailUrl = item.ThumbnailUrl,
            DisplayName = item.DisplayName,
            Artist = item.Artist,
            Album = item.Album,
            DurationSeconds = item.DurationSeconds,
            Status = item.Status
        };
}
