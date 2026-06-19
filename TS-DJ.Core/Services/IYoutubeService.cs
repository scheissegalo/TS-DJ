using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

/// <summary>
/// Unified YouTube integration API: diagnostics, metadata, playlists, and playback extraction.
/// </summary>
public interface IYoutubeService
{
    Task<YoutubeDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default);

    Task RefreshDiagnosticsAsync(CancellationToken cancellationToken = default);

    Task<YoutubeVideoMetadata> FetchVideoMetadataAsync(string videoUrl, CancellationToken cancellationToken = default);

    Task<YoutubePlaylistMetadata> FetchPlaylistMetadataAsync(string inputUrl, CancellationToken cancellationToken = default);

    Task<IPlaybackStreamHandle> ExtractAudioAsync(string videoUrl, CancellationToken cancellationToken = default);
}
