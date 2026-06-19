using TS_DJ.Core.Services;

namespace TS_DJ.Core.Models;

public enum MediaLoadKind
{
    LocalFile,
    RemoteStreamUrl,
    BufferedStream
}

/// <summary>
/// Resolved play-time media ready for a deck decoder.
/// </summary>
public sealed class MediaLoadResult
{
    public MediaLoadKind Kind { get; init; }
    public string? LocalFilePath { get; init; }
    public string? StreamUrl { get; init; }
    public IPlaybackStreamHandle? StreamHandle { get; init; }

    public static MediaLoadResult FromLocalFile(string filePath) =>
        new() { Kind = MediaLoadKind.LocalFile, LocalFilePath = filePath };

    public static MediaLoadResult FromRemoteUrl(string streamUrl) =>
        new() { Kind = MediaLoadKind.RemoteStreamUrl, StreamUrl = streamUrl };

    public static MediaLoadResult FromBufferedStream(IPlaybackStreamHandle handle) =>
        new() { Kind = MediaLoadKind.BufferedStream, StreamHandle = handle };
}
