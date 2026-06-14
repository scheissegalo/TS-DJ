using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Infrastructure.Playlists;
using TS_DJ.Infrastructure.Settings;

var dbPath = Path.Combine(Path.GetTempPath(), $"ts-dj-verify-{Guid.NewGuid():N}.db");
using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
var logger = loggerFactory.CreateLogger<SqliteSettingsService>();
var playlistLogger = loggerFactory.CreateLogger<PlaylistService>();

try
{
    using var service = new SqliteSettingsService(dbPath, logger);
    var playlistService = new PlaylistService(service, playlistLogger);

    var original = new ConnectionSettings
    {
        Address = "127.0.0.1:9987",
        Nickname = "VerifyBot",
        ServerPassword = "secret",
        Channel = "/1",
        IdentityOffset = 42,
        SecurityLevel = 8
    };

    await service.SaveConnectionSettingsAsync(original);
    var loaded = await service.LoadConnectionSettingsAsync();

    if (loaded.Address != original.Address ||
        loaded.Nickname != original.Nickname ||
        loaded.ServerPassword != original.ServerPassword ||
        loaded.Channel != original.Channel ||
        loaded.IdentityOffset != original.IdentityOffset)
    {
        Console.Error.WriteLine("Settings round-trip mismatch.");
        Console.Error.WriteLine($"Expected address={original.Address}, got {loaded.Address}");
        Environment.Exit(1);
    }

    await service.SetSettingAsync("test.key", "test-value");
    var value = await service.GetSettingAsync("test.key");
    if (value != "test-value")
    {
        Console.Error.WriteLine($"Single setting mismatch: expected 'test-value', got '{value}'");
        Environment.Exit(1);
    }

    var profiles = new TeamSpeakConnectionProfilesSettings
    {
        Profiles =
        [
            new TeamSpeakConnectionProfile
            {
                Name = "Test Server",
                Address = "ts.example.com",
                Nickname = "DJ",
                ServerPassword = "pw",
                DefaultChannel = "/5"
            }
        ],
        SelectedProfileId = null
    };
    profiles.SelectedProfileId = profiles.Profiles[0].Id;

    await service.SaveTeamSpeakConnectionProfilesAsync(profiles);
    var loadedProfiles = await service.LoadTeamSpeakConnectionProfilesAsync();
    if (loadedProfiles.Profiles.Count != 1 ||
        loadedProfiles.Profiles[0].Address != "ts.example.com" ||
        loadedProfiles.SelectedProfileId != profiles.Profiles[0].Id)
    {
        Console.Error.WriteLine("Connection profiles round-trip mismatch.");
        Environment.Exit(1);
    }

    var queue = new[]
    {
        PlaybackQueueItem.FromLocalFile("/tmp/a.mp3", "Track A"),
        new PlaybackQueueItem
        {
            SourceKind = PlaybackSourceKind.RemoteStream,
            RemoteTrackId = "remote-1",
            DisplayName = "Remote Track",
            Artist = "Artist",
            Album = "Album",
            DurationSeconds = 200
        }
    };

    var saved = await playlistService.SaveFromQueueAsync("Verify Playlist", queue, "test description");
    var listed = await playlistService.ListAsync();
    if (listed.Count != 1 || listed[0].Name != "Verify Playlist" || listed[0].EntryCount != 2)
    {
        Console.Error.WriteLine("Playlist list mismatch after save.");
        Environment.Exit(1);
    }

    var loadedPlaylist = await playlistService.GetAsync(saved.Id);
    if (loadedPlaylist is null || loadedPlaylist.Entries.Count != 2)
    {
        Console.Error.WriteLine("Playlist payload mismatch.");
        Environment.Exit(1);
    }

    var resolved = playlistService.ResolveEntries(loadedPlaylist);
    if (resolved.Count != 2 ||
        resolved[0].SourceKind != PlaybackSourceKind.LocalFile ||
        resolved[1].RemoteTrackId != "remote-1")
    {
        Console.Error.WriteLine("Playlist resolve mismatch.");
        Environment.Exit(1);
    }

    Console.WriteLine("Settings smoke test passed.");
}
finally
{
    // Windows CI keeps pooled SQLite handles open after connections are disposed.
    SqliteConnection.ClearAllPools();
    foreach (var path in new[] { dbPath, $"{dbPath}-wal", $"{dbPath}-shm" })
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
