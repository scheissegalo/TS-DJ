using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Playlists;

public sealed class PlaylistService : IPlaylistService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<PlaylistService> _logger;

    public PlaylistService(ISettingsService settingsService, ILogger<PlaylistService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SavedPlaylistSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var library = await _settingsService.LoadPlaylistLibraryAsync(cancellationToken);
        return library.Playlists
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<SavedPlaylist?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _settingsService.LoadSavedPlaylistAsync(id, cancellationToken);

    public async Task<SavedPlaylist> SaveFromQueueAsync(
        string name,
        IEnumerable<PlaybackQueueItem> queue,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Playlist name is required.", nameof(name));

        var entries = queue.Select(SavedMediaEntry.FromQueueItem).ToList();
        var now = DateTime.UtcNow;
        var playlist = new SavedPlaylist
        {
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedUtc = now,
            ModifiedUtc = now,
            Entries = entries
        };

        await _settingsService.SaveSavedPlaylistAsync(playlist, cancellationToken);

        var library = await _settingsService.LoadPlaylistLibraryAsync(cancellationToken);
        library.Playlists.RemoveAll(p => p.Id == playlist.Id);
        library.Playlists.Add(SavedPlaylistSummary.FromPlaylist(playlist));
        await _settingsService.SavePlaylistLibraryAsync(library, cancellationToken);

        _logger.LogInformation("Saved playlist '{Name}' with {Count} entries", playlist.Name, entries.Count);
        return playlist;
    }

    public async Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Playlist name is required.", nameof(name));

        var playlist = await _settingsService.LoadSavedPlaylistAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Playlist {id} not found.");

        playlist.Name = name.Trim();
        playlist.ModifiedUtc = DateTime.UtcNow;
        await _settingsService.SaveSavedPlaylistAsync(playlist, cancellationToken);
        await UpdateLibrarySummaryAsync(playlist, cancellationToken);
    }

    public async Task UpdateMetadataAsync(Guid id, string? description, CancellationToken cancellationToken = default)
    {
        var playlist = await _settingsService.LoadSavedPlaylistAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Playlist {id} not found.");

        playlist.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        playlist.ModifiedUtc = DateTime.UtcNow;
        await _settingsService.SaveSavedPlaylistAsync(playlist, cancellationToken);
        await UpdateLibrarySummaryAsync(playlist, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _settingsService.DeleteSavedPlaylistAsync(id, cancellationToken);

        var library = await _settingsService.LoadPlaylistLibraryAsync(cancellationToken);
        library.Playlists.RemoveAll(p => p.Id == id);
        await _settingsService.SavePlaylistLibraryAsync(library, cancellationToken);

        _logger.LogInformation("Deleted playlist {PlaylistId}", id);
    }

    public IReadOnlyList<PlaybackQueueItem> ResolveEntries(SavedPlaylist playlist) =>
        playlist.Entries.Select(e => e.ToQueueItem()).ToList();

    private async Task UpdateLibrarySummaryAsync(SavedPlaylist playlist, CancellationToken cancellationToken)
    {
        var library = await _settingsService.LoadPlaylistLibraryAsync(cancellationToken);
        var index = library.Playlists.FindIndex(p => p.Id == playlist.Id);
        var summary = SavedPlaylistSummary.FromPlaylist(playlist);

        if (index >= 0)
            library.Playlists[index] = summary;
        else
            library.Playlists.Add(summary);

        await _settingsService.SavePlaylistLibraryAsync(library, cancellationToken);
    }
}
