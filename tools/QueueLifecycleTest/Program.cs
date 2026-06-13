using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak.DependencyInjection;

var wavPath = args.Length > 0 ? args[0] : "/tmp/track1.wav";
var mp3Path = args.Length > 1 ? args[1] : "/tmp/ts-dj-test.mp3";
var address = args.Length > 2 ? args[2] : "127.0.0.1";
var channel = args.Length > 3 ? args[3] : "/1";

if (!File.Exists(wavPath))
{
    Console.Error.WriteLine($"WAV not found: {wavPath}");
    return 1;
}

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
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
services.AddTsDjInfrastructure();
services.AddTsDjTeamSpeak();
services.AddTsDjAudio();

using var provider = services.BuildServiceProvider();
var teamSpeak = provider.GetRequiredService<ITeamSpeakService>();
var playback = provider.GetRequiredService<IAudioPlaybackService>();
var mixer = provider.GetRequiredService<IAudioMixerService>();

var settings = new ConnectionSettings
{
    Address = address,
    Nickname = "TS-DJ-QueueTest",
    Channel = channel
};

Console.WriteLine($"Connecting to {address} channel {channel}…");
await teamSpeak.ConnectAsync(settings);

if (teamSpeak.State != ConnectionState.Connected)
{
    Console.Error.WriteLine($"Connect failed — state: {teamSpeak.State}");
    return 1;
}

Console.WriteLine("Connected. Enqueuing WAV then MP3…");
await playback.LoadAsync(wavPath);
await playback.LoadAsync(mp3Path);

Console.WriteLine("Starting playback…");
await playback.PlayAsync();

if (playback.State != PlaybackState.Playing)
{
    Console.Error.WriteLine($"Playback failed to start — state: {playback.State}");
    await teamSpeak.DisconnectAsync();
    return 1;
}

Console.WriteLine($"Track 1 playing: {mixer.NowPlaying?.DisplayName}");

var deadline = DateTime.UtcNow.AddSeconds(30);
while (DateTime.UtcNow < deadline)
{
    if (mixer.NowPlaying?.FilePath == mp3Path && mixer.Music.IsPlaying)
    {
        Console.WriteLine($"Track 2 auto-started: {mixer.NowPlaying.DisplayName}");
        break;
    }

    await Task.Delay(200);
}

if (mixer.NowPlaying?.FilePath != mp3Path)
{
    Console.Error.WriteLine("FAIL: MP3 did not auto-start after WAV EOF");
    Console.Error.WriteLine($"NowPlaying={mixer.NowPlaying?.FilePath ?? "null"}, Music.IsPlaying={mixer.Music.IsPlaying}");
    foreach (var item in mixer.Queue)
        Console.Error.WriteLine($"  Queue: {item.DisplayName} [{item.Status}]");
    await teamSpeak.DisconnectAsync();
    return 1;
}

deadline = DateTime.UtcNow.AddSeconds(120);
while (DateTime.UtcNow < deadline)
{
    if (!mixer.Music.IsPlaying && mixer.NowPlaying is null)
    {
        Console.WriteLine("Playback stopped after final track");
        break;
    }

    await Task.Delay(200);
}

if (mixer.Music.IsPlaying || mixer.NowPlaying is not null)
{
    Console.Error.WriteLine($"FAIL: still active after queue finished — IsPlaying={mixer.Music.IsPlaying}, NowPlaying={mixer.NowPlaying?.DisplayName ?? "null"}");
    await teamSpeak.DisconnectAsync();
    return 1;
}

// Allow pipeline to settle
await Task.Delay(500);

Console.WriteLine("Queue lifecycle test passed.");
await teamSpeak.DisconnectAsync();
return 0;
