using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Navidrome;

public sealed class NavidromeService : INavidromeService, IPlaybackStreamUrlProvider
{
    public const string HttpClientName = "Navidrome";

    private const string ClientId = "TS-DJ";
    private const string ApiVersion = "1.16.1";

    private readonly HttpClient _httpClient;
    private readonly ILogger<NavidromeService> _logger;
    private NavidromeSettings _settings = new();
    private bool _authenticated;

    public NavidromeService(HttpClient httpClient, ILogger<NavidromeService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(ClientId);
    }

    public bool IsAuthenticated => _authenticated;

    public async Task AuthenticateAsync(NavidromeSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = NormalizeSettings(settings);
        var url = BuildApiUrl(normalized.ServerUrl, "ping", normalized.Username, normalized.Password);

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureOkResponse(body, "Authentication failed");

        _settings = normalized;
        _authenticated = true;
        _logger.LogInformation("Navidrome authenticated for user {Username} at {ServerUrl}",
            normalized.Username, normalized.ServerUrl);
    }

    public async Task<NavidromeSearchResults> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var url = BuildApiUrl(_settings.ServerUrl, "search3", _settings.Username, _settings.Password,
            ("query", query),
            ("songCount", "50"),
            ("albumCount", "30"),
            ("artistCount", "30"));

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = EnsureOkResponse(body, "Search failed");

        var searchResult = FindChild(root, "searchResult3");
        if (searchResult is null)
        {
            _logger.LogWarning("Search response missing searchResult3 element");
            return new NavidromeSearchResults();
        }

        var tracks = FindChildren(searchResult, "song").Select(ParseTrack).ToList();
        var albums = FindChildren(searchResult, "album").Select(ParseAlbum).ToList();
        var artists = FindChildren(searchResult, "artist").Select(ParseArtist).ToList();

        _logger.LogDebug("Search parsed {TrackCount} tracks, {AlbumCount} albums, {ArtistCount} artists",
            tracks.Count, albums.Count, artists.Count);

        return new NavidromeSearchResults
        {
            Tracks = tracks,
            Albums = albums,
            Artists = artists
        };
    }

    public async Task<IReadOnlyList<NavidromePlaylist>> GetPlaylistsAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        var url = BuildApiUrl(_settings.ServerUrl, "getPlaylists", _settings.Username, _settings.Password);
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = EnsureOkResponse(body, "Failed to load playlists");

        return FindChild(root, "playlists")?
            .Elements()
            .Where(e => e.Name.LocalName == "playlist")
            .Select(ParsePlaylist)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<NavidromeTrack>> GetAlbumTracksAsync(
        string albumId,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        if (string.IsNullOrWhiteSpace(albumId))
            throw new ArgumentException("Album id is required.", nameof(albumId));

        var url = BuildApiUrl(_settings.ServerUrl, "getAlbum", _settings.Username, _settings.Password,
            ("id", albumId));

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = EnsureOkResponse(body, "Failed to load album");

        var album = FindChild(root, "album");
        if (album is null)
            return [];

        return ParseAlbumTracks(album);
    }

    public async Task<IReadOnlyList<NavidromeTrack>> GetArtistTracksAsync(
        string artistId,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        if (string.IsNullOrWhiteSpace(artistId))
            throw new ArgumentException("Artist id is required.", nameof(artistId));

        var url = BuildApiUrl(_settings.ServerUrl, "getArtist", _settings.Username, _settings.Password,
            ("id", artistId));

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = EnsureOkResponse(body, "Failed to load artist");

        var artist = FindChild(root, "artist");
        if (artist is null)
            return [];

        var albums = artist.Elements()
            .Where(e => e.Name.LocalName == "album")
            .Select(ParseAlbum)
            .OrderBy(a => a.Year)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tracks = new List<NavidromeTrack>();
        foreach (var album in albums)
        {
            var albumTracks = await GetAlbumTracksAsync(album.Id, cancellationToken);
            tracks.AddRange(albumTracks);
        }

        return tracks;
    }

    public Task<IReadOnlyList<NavidromeTrack>> ResolveTracksAsync(
        NavidromeMediaKind mediaKind,
        string entityId,
        CancellationToken cancellationToken = default) =>
        mediaKind switch
        {
            NavidromeMediaKind.Track => throw new ArgumentException(
                "Use a single track directly for track media kind.", nameof(mediaKind)),
            NavidromeMediaKind.Album => GetAlbumTracksAsync(entityId, cancellationToken),
            NavidromeMediaKind.Artist => GetArtistTracksAsync(entityId, cancellationToken),
            NavidromeMediaKind.Playlist => GetPlaylistTracksAsync(entityId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mediaKind), mediaKind, "Unknown media kind.")
        };

    public async Task<IReadOnlyList<NavidromeTrack>> GetPlaylistTracksAsync(
        string playlistId,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        if (string.IsNullOrWhiteSpace(playlistId))
            throw new ArgumentException("Playlist id is required.", nameof(playlistId));

        var url = BuildApiUrl(_settings.ServerUrl, "getPlaylist", _settings.Username, _settings.Password,
            ("id", playlistId));

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = EnsureOkResponse(body, "Failed to load playlist");

        return FindChild(root, "playlist")?
            .Elements()
            .Where(e => e.Name.LocalName is "entry" or "song")
            .Select(ParseTrack)
            .ToList() ?? [];
    }

    public string BuildStreamUrl(string trackId)
    {
        EnsureAuthenticated();

        if (string.IsNullOrWhiteSpace(trackId))
            throw new ArgumentException("Track id is required.", nameof(trackId));

        var (salt, token) = CreateAuthToken(_settings.Password);
        var baseUrl = NormalizeServerUrl(_settings.ServerUrl);
        var query = new StringBuilder();
        query.Append("id=").Append(Uri.EscapeDataString(trackId));
        query.Append("&u=").Append(Uri.EscapeDataString(_settings.Username));
        query.Append("&t=").Append(token);
        query.Append("&s=").Append(Uri.EscapeDataString(salt));
        query.Append("&v=").Append(ApiVersion);
        query.Append("&c=").Append(Uri.EscapeDataString(ClientId));
        query.Append("&format=mp3");

        return $"{baseUrl}/rest/stream?{query}";
    }

    public PlaybackQueueItem CreateQueueItem(NavidromeTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new PlaybackQueueItem
        {
            SourceKind = PlaybackSourceKind.RemoteStream,
            RemoteTrackId = track.Id,
            DisplayName = track.DisplayName,
            Artist = track.Artist,
            Album = track.Album,
            DurationSeconds = track.DurationSeconds,
            Status = PlaybackQueueStatus.Queued
        };
    }

    public string? TryGetStreamUrl(PlaybackQueueItem item)
    {
        if (item.SourceKind != PlaybackSourceKind.RemoteStream || string.IsNullOrWhiteSpace(item.RemoteTrackId))
            return null;

        if (!_authenticated)
            return null;

        return BuildStreamUrl(item.RemoteTrackId);
    }

    private void EnsureAuthenticated()
    {
        if (!_authenticated)
            throw new InvalidOperationException("Not connected to Navidrome. Authenticate first.");
    }

    private static NavidromeSettings NormalizeSettings(NavidromeSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerUrl))
            throw new ArgumentException("Navidrome server URL is required.", nameof(settings));

        if (string.IsNullOrWhiteSpace(settings.Username))
            throw new ArgumentException("Navidrome username is required.", nameof(settings));

        if (string.IsNullOrWhiteSpace(settings.Password))
            throw new ArgumentException("Navidrome password is required.", nameof(settings));

        return new NavidromeSettings
        {
            ServerUrl = NormalizeServerUrl(settings.ServerUrl),
            Username = settings.Username.Trim(),
            Password = settings.Password
        };
    }

    private static string NormalizeServerUrl(string serverUrl)
    {
        var trimmed = serverUrl.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }

        return trimmed;
    }

    private static string BuildApiUrl(
        string serverUrl,
        string endpoint,
        string username,
        string password,
        params (string Key, string Value)[] extra)
    {
        var (salt, token) = CreateAuthToken(password);
        var baseUrl = NormalizeServerUrl(serverUrl);
        var query = new StringBuilder();
        query.Append("u=").Append(Uri.EscapeDataString(username));
        query.Append("&t=").Append(token);
        query.Append("&s=").Append(Uri.EscapeDataString(salt));
        query.Append("&v=").Append(ApiVersion);
        query.Append("&c=").Append(Uri.EscapeDataString(ClientId));

        foreach (var (key, value) in extra)
        {
            query.Append('&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return $"{baseUrl}/rest/{endpoint}?{query}";
    }

    private static (string Salt, string Token) CreateAuthToken(string password)
    {
        var salt = GenerateSalt();
        var token = ComputeMd5Hex(password + salt);
        return (salt, token);
    }

    private static string GenerateSalt()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeMd5Hex(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static XElement EnsureOkResponse(string body, string failureMessage)
    {
        var doc = XDocument.Parse(body);
        var root = doc.Root ?? throw new InvalidOperationException($"{failureMessage}: empty response");

        var status = root.Attribute("status")?.Value;
        if (status == "ok")
            return root;

        var error = root.Elements().FirstOrDefault(e => e.Name.LocalName == "error");
        var code = error?.Attribute("code")?.Value ?? "unknown";
        var message = error?.Attribute("message")?.Value ?? failureMessage;
        throw new InvalidOperationException($"{failureMessage} ({code}): {message}");
    }

    private static NavidromeTrack ParseTrack(XElement element) =>
        new()
        {
            Id = element.Attribute("id")?.Value ?? string.Empty,
            Title = element.Attribute("title")?.Value ?? "Unknown",
            Artist = element.Attribute("artist")?.Value ?? string.Empty,
            Album = element.Attribute("album")?.Value ?? string.Empty,
            DurationSeconds = ParseInt(element.Attribute("duration")?.Value),
            TrackNumber = ParseInt(element.Attribute("track")?.Value)
        };

    private static List<NavidromeTrack> ParseAlbumTracks(XElement albumElement)
    {
        return albumElement.Elements()
            .Where(e => e.Name.LocalName is "song" or "child")
            .Select(ParseTrack)
            .OrderBy(t => t.TrackNumber <= 0 ? int.MaxValue : t.TrackNumber)
            .ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static NavidromeAlbum ParseAlbum(XElement element) =>
        new()
        {
            Id = element.Attribute("id")?.Value ?? string.Empty,
            Name = element.Attribute("name")?.Value ?? element.Attribute("title")?.Value ?? "Unknown",
            Artist = element.Attribute("artist")?.Value ?? string.Empty,
            SongCount = ParseInt(element.Attribute("songCount")?.Value),
            DurationSeconds = ParseInt(element.Attribute("duration")?.Value),
            Year = ParseInt(element.Attribute("year")?.Value)
        };

    private static NavidromeArtist ParseArtist(XElement element) =>
        new()
        {
            Id = element.Attribute("id")?.Value ?? string.Empty,
            Name = element.Attribute("name")?.Value ?? "Unknown",
            AlbumCount = ParseInt(element.Attribute("albumCount")?.Value)
        };

    private static NavidromePlaylist ParsePlaylist(XElement element) =>
        new()
        {
            Id = element.Attribute("id")?.Value ?? string.Empty,
            Name = element.Attribute("name")?.Value ?? "Unknown",
            SongCount = ParseInt(element.Attribute("songCount")?.Value),
            DurationSeconds = ParseInt(element.Attribute("duration")?.Value)
        };

    private static int ParseInt(string? raw) =>
        int.TryParse(raw, out var value) ? value : 0;

    /// <summary>
    /// Subsonic XML uses a default namespace; match by local name to avoid empty results.
    /// </summary>
    private static XElement? FindChild(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static IEnumerable<XElement> FindChildren(XElement parent, string localName) =>
        parent.Elements().Where(e => e.Name.LocalName == localName);
}
