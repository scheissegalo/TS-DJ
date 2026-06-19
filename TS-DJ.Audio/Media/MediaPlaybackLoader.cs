using Microsoft.Extensions.Logging;
using TS_DJ.Audio.Decoding;
using TS_DJ.Audio.Playback;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Audio.Media;

public sealed class MediaPlaybackLoader : IMediaPlaybackLoader
{
    private readonly ILogger<MediaPlaybackLoader> _logger;
    private readonly IMediaSourceRegistry? _mediaRegistry;
    private readonly IPlaybackStreamResolver? _streamResolver;
    private readonly IPlaybackStreamOpener? _streamOpener;
    private readonly MediaLoadTimingTracker? _timing;

    public MediaPlaybackLoader(
        ILogger<MediaPlaybackLoader> logger,
        IMediaSourceRegistry? mediaRegistry = null,
        IPlaybackStreamResolver? streamResolver = null,
        IPlaybackStreamOpener? streamOpener = null,
        MediaLoadTimingTracker? timing = null)
    {
        _logger = logger;
        _mediaRegistry = mediaRegistry;
        _streamResolver = streamResolver;
        _streamOpener = streamOpener;
        _timing = timing;
    }

    public async Task<MediaLoadResult> LoadAsync(
        PlaybackQueueItem item,
        IPlaybackStreamHandle? prefetchedStream = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await LoadCoreAsync(item, prefetchedStream, cancellationToken);
            _timing?.RecordSuccess(item.SourceKind, sw.Elapsed);
            return result;
        }
        catch
        {
            _timing?.RecordFailure(item.SourceKind, sw.Elapsed);
            throw;
        }
    }

    private async Task<MediaLoadResult> LoadCoreAsync(
        PlaybackQueueItem item,
        IPlaybackStreamHandle? prefetchedStream,
        CancellationToken cancellationToken)
    {
        if (item.SourceKind == PlaybackSourceKind.LocalFile)
        {
            ValidateLocalFile(item);
            return MediaLoadResult.FromLocalFile(item.FilePath);
        }

        if (item.SourceKind == PlaybackSourceKind.YouTube)
        {
            if (string.IsNullOrWhiteSpace(item.VideoUrl))
                throw new ArgumentException("YouTube queue items require a video URL.", nameof(item));

            var handle = prefetchedStream;
            if (handle is null)
            {
                if (_streamOpener is null || !_streamOpener.CanOpen(item))
                    throw new InvalidOperationException("YouTube playback is not configured.");

                handle = await _streamOpener.OpenPlaybackStreamAsync(item, cancellationToken);
            }

            if (handle is null)
                throw new InvalidOperationException("Could not open YouTube stream.");

            return MediaLoadResult.FromBufferedStream(handle);
        }

        if (prefetchedStream is not null)
            return MediaLoadResult.FromBufferedStream(prefetchedStream);

        var streamUrl = _streamResolver is null
            ? null
            : await _streamResolver.ResolveStreamUrlAsync(item, cancellationToken);

        if (string.IsNullOrWhiteSpace(streamUrl))
            throw new InvalidOperationException("Could not resolve remote stream URL.");

        _logger.LogDebug("Buffering remote stream for {SourceKey}", item.SourceKey);
        var memory = await RemoteAudioHttp.DownloadAsSeekableStreamAsync(streamUrl, cancellationToken);
        var bytes = memory.ToArray();
        return MediaLoadResult.FromBufferedStream(new ByteArrayPlaybackStreamHandle(bytes));
    }

    private void ValidateLocalFile(PlaybackQueueItem item)
    {
        if (!File.Exists(item.FilePath))
            throw new FileNotFoundException("Audio file not found.", item.FilePath);

        if (!Decoding.AudioFileDecoder.IsSupportedFile(item.FilePath))
        {
            throw new NotSupportedException(
                $"Unsupported audio format '{Path.GetExtension(item.FilePath)}'. Supported: MP3, WAV, AIFF, FLAC.");
        }
    }
}
