using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak.DependencyInjection;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: ThreeTrackQueueTest <wav> <mp3a> <mp3b>");
    return 1;
}

var wav = args[0];
var mp3a = args[1];
var mp3b = args[2];

foreach (var f in new[] { wav, mp3a, mp3b })
{
    if (!File.Exists(f))
    {
        Console.Error.WriteLine($"Missing: {f}");
        return 1;
    }
}

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddTsDjInfrastructure();
services.AddTsDjTeamSpeak();
services.AddTsDjAudio();

using var provider = services.BuildServiceProvider();
var teamSpeak = provider.GetRequiredService<ITeamSpeakService>();
var playback = provider.GetRequiredService<IAudioPlaybackService>();
var mixer = provider.GetRequiredService<IAudioMixerService>();

await teamSpeak.ConnectAsync(new ConnectionSettings
{
    Address = "127.0.0.1",
    Nickname = "ThreeTrackTest",
    Channel = "/1"
});

// Simulate UI: browse all three while first plays
await playback.LoadAsync(wav);
await playback.PlayAsync();
Console.WriteLine($"After browse 1: queue={mixer.Queue.Count}, now={mixer.NowPlaying?.DisplayName}");

await Task.Delay(500);
await playback.LoadAsync(mp3a);
Console.WriteLine($"After browse 2: queue={mixer.Queue.Count}, queued={mixer.Queue.Count(i => i.Status == PlaybackQueueStatus.Queued)}");
foreach (var item in mixer.Queue)
    Console.WriteLine($"  {item.DisplayName} [{item.Status}]");

await Task.Delay(500);
await playback.LoadAsync(mp3b);
Console.WriteLine($"After browse 3: queue={mixer.Queue.Count}, queued={mixer.Queue.Count(i => i.Status == PlaybackQueueStatus.Queued)}");
foreach (var item in mixer.Queue)
    Console.WriteLine($"  {item.DisplayName} [{item.Status}]");

// Wait for track 3 to start (validates queue auto-advance; full playback is optional)
var deadline = DateTime.UtcNow.AddMinutes(5);
string? lastNowPlaying = null;
var track3Started = false;
while (DateTime.UtcNow < deadline)
{
    var np = mixer.NowPlaying?.FilePath;
    if (np != lastNowPlaying)
    {
        Console.WriteLine($"NowPlaying -> {mixer.NowPlaying?.DisplayName ?? "none"} (IsPlaying={mixer.Music.IsPlaying})");
        foreach (var item in mixer.Queue)
            Console.WriteLine($"  {item.DisplayName} [{item.Status}]");
        lastNowPlaying = np;
    }

    if (mixer.NowPlaying?.FilePath == mp3b && mixer.Music.IsPlaying)
    {
        track3Started = true;
        Console.WriteLine("Track 3 started — queue auto-advance confirmed");
        break;
    }

    if (mixer.Queue.All(i => i.Status is PlaybackQueueStatus.Played or PlaybackQueueStatus.Failed)
        && !mixer.Music.IsPlaying)
    {
        Console.WriteLine("All tracks finished");
        track3Started = mixer.Queue.Any(i => i.FilePath == mp3b && i.Status == PlaybackQueueStatus.Played);
        break;
    }

    await Task.Delay(200);
}

var track3 = mixer.Queue.FirstOrDefault(i => i.FilePath == mp3b);
Console.WriteLine($"Track3 status: {track3?.Status}, started={track3Started}");

await teamSpeak.DisconnectAsync();

if (!track3Started)
{
    Console.Error.WriteLine("FAIL: third track never started");
    return 1;
}

Console.WriteLine("PASS");
return 0;
