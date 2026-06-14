namespace TS_DJ.Core.Models;

public sealed class PlaylistLibrary
{
    public const string IndexKey = "playlists.index";
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<SavedPlaylistSummary> Playlists { get; set; } = [];
}

public sealed class SavedPlaylistSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public int EntryCount { get; set; }

    public static SavedPlaylistSummary FromPlaylist(SavedPlaylist playlist) =>
        new()
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            CreatedUtc = playlist.CreatedUtc,
            ModifiedUtc = playlist.ModifiedUtc,
            EntryCount = playlist.Entries.Count
        };
}
