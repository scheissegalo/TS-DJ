using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak.DependencyInjection;

var mp3Path = args.Length > 0 ? args[0] : "/tmp/ts-dj-test.mp3";
var address = args.Length > 1 ? args[1] : "127.0.0.1";
var channel = args.Length > 2 ? args[2] : "/1";

if (!File.Exists(mp3Path))
{
    Console.Error.WriteLine($"MP3 not found: {mp3Path}");
    return 1;
}

if (!TSLib.Audio.Opus.NativeMethods.PreloadLibrary())
{
    Console.Error.WriteLine("Failed to load libopus");
    return 1;
}

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddTsDjInfrastructure();
services.AddTsDjTeamSpeak();
services.AddTsDjAudio();

using var provider = services.BuildServiceProvider();
var teamSpeak = provider.GetRequiredService<ITeamSpeakService>();
var playback = provider.GetRequiredService<IAudioPlaybackService>();

var settings = new ConnectionSettings
{
    Address = address,
    Nickname = "TS-DJ-SmokeTest",
    Channel = channel
};

Console.WriteLine($"Connecting to {address} channel {channel}…");
await teamSpeak.ConnectAsync(settings);

if (teamSpeak.State != ConnectionState.Connected)
{
    Console.Error.WriteLine($"Connect failed — state: {teamSpeak.State}");
    return 1;
}

Console.WriteLine("Connected. Starting playback…");
await playback.LoadAsync(mp3Path);
await playback.PlayAsync();

if (playback.State != PlaybackState.Playing)
{
    Console.Error.WriteLine($"Playback failed to start — state: {playback.State}");
    await teamSpeak.DisconnectAsync();
    return 1;
}

Console.WriteLine("Streaming for 5 seconds…");
await Task.Delay(TimeSpan.FromSeconds(5));

await playback.StopAsync();
await teamSpeak.DisconnectAsync();

Console.WriteLine("Smoke test passed.");
return 0;
