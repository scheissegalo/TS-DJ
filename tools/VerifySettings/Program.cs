using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Infrastructure.Settings;

var dbPath = Path.Combine(Path.GetTempPath(), $"ts-dj-verify-{Guid.NewGuid():N}.db");
using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
var logger = loggerFactory.CreateLogger<SqliteSettingsService>();

try
{
    var service = new SqliteSettingsService(dbPath, logger);

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

    Console.WriteLine("Settings smoke test passed.");
}
finally
{
    if (File.Exists(dbPath))
        File.Delete(dbPath);
}
