using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface INavidromeService
{
    bool IsAuthenticated { get; }

    Task AuthenticateAsync(NavidromeSettings settings, CancellationToken cancellationToken = default);

    Task<NavidromeSearchResults> SearchAsync(string query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NavidromePlaylist>> GetPlaylistsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NavidromeTrack>> GetPlaylistTracksAsync(string playlistId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NavidromeTrack>> GetAlbumTracksAsync(string albumId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NavidromeTrack>> GetArtistTracksAsync(string artistId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NavidromeTrack>> ResolveTracksAsync(
        NavidromeMediaKind mediaKind,
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a fresh authenticated stream URL for the given track id.
    /// Does not log credentials or tokens.
    /// </summary>
    string BuildStreamUrl(string trackId);

    PlaybackQueueItem CreateQueueItem(NavidromeTrack track);
}

public interface INavidromeMediaQueueService
{
    Task<int> EnqueueTracksAsync(IReadOnlyList<NavidromeTrack> tracks, bool playImmediately);
}
