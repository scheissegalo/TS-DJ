using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Media;

public sealed class LocalFileMediaSource : IMediaSource
{
    public PlaybackSourceKind SourceKind => PlaybackSourceKind.LocalFile;

    public bool CanHandleInput(string input) =>
        !string.IsNullOrWhiteSpace(input)
        && !input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public bool CanHandleItem(PlaybackQueueItem item) =>
        item.SourceKind == PlaybackSourceKind.LocalFile;

    public void ValidateForEnqueue(PlaybackQueueItem item)
    {
        if (item.SourceKind != PlaybackSourceKind.LocalFile)
            throw new ArgumentException("Not a local file queue item.", nameof(item));

        if (!File.Exists(item.FilePath))
            throw new FileNotFoundException("Audio file not found.", item.FilePath);

        if (!IsSupportedFile(item.FilePath))
        {
            throw new NotSupportedException(
                $"Unsupported audio format '{Path.GetExtension(item.FilePath)}'. Supported: MP3, WAV, AIFF, FLAC.");
        }
    }

    public Task<PlaybackQueueItem?> TryCreateFromInputAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!CanHandleInput(input))
            return Task.FromResult<PlaybackQueueItem?>(null);

        var path = Path.GetFullPath(input.Trim());
        if (!File.Exists(path))
            return Task.FromResult<PlaybackQueueItem?>(null);

        return Task.FromResult<PlaybackQueueItem?>(PlaybackQueueItem.FromLocalFile(path));
    }

    public Task<string?> ResolveStreamUrlAsync(PlaybackQueueItem item, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    private static bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".aiff" or ".aif" or ".flac";
    }
}
