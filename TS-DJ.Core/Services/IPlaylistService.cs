using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface IPlaylistService
{
    Task<IReadOnlyList<SavedPlaylistSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<SavedPlaylist?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SavedPlaylist> SaveFromQueueAsync(
        string name,
        IEnumerable<PlaybackQueueItem> queue,
        string? description = null,
        CancellationToken cancellationToken = default);
    Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default);
    Task UpdateMetadataAsync(Guid id, string? description, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    IReadOnlyList<PlaybackQueueItem> ResolveEntries(SavedPlaylist playlist);
}
