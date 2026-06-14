namespace TS_DJ.Core.Models;

public sealed class SavedPlaylist
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<SavedMediaEntry> Entries { get; set; } = [];

    public static string StorageKey(Guid id) => $"playlists.{id:D}";
}
