namespace TS_DJ.Core.Models;

public sealed class NavidromeTrack
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Artist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public int DurationSeconds { get; init; }
    public int TrackNumber { get; init; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Artist)
            ? Title
            : $"{Artist} — {Title}";

    public TimeSpan Duration => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds));
}

public sealed class NavidromeAlbum
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Artist { get; init; } = string.Empty;
    public int SongCount { get; init; }
    public int DurationSeconds { get; init; }
    public int Year { get; init; }
}

public sealed class NavidromeArtist
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int AlbumCount { get; init; }
}

public sealed class NavidromePlaylist
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int SongCount { get; init; }
    public int DurationSeconds { get; init; }
}

public sealed class NavidromeSearchResults
{
    public IReadOnlyList<NavidromeTrack> Tracks { get; init; } = [];
    public IReadOnlyList<NavidromeAlbum> Albums { get; init; } = [];
    public IReadOnlyList<NavidromeArtist> Artists { get; init; } = [];
}
