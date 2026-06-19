using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Audio.Mixing;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak.DependencyInjection;
using TSLib.Audio;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: DeckCrossfadeTest <track-a> <track-b>");
    return 1;
}

var trackA = args[0];
var trackB = args[1];
foreach (var path in new[] { trackA, trackB })
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 1;
    }
}

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTsDjInfrastructure();
services.AddTsDjTeamSpeak();
services.AddTsDjAudio();

await using var provider = services.BuildServiceProvider();
var mixer = (AudioMixerService)provider.GetRequiredService<IAudioMixerService>();

mixer.SetPlaybackSettings(new PlaybackSettings
{
    Mode = PlaybackMode.DualDeck,
    CrossfadeEnabled = true,
    CrossfadeDurationSeconds = 1.0
});

var itemA = PlaybackQueueItem.FromLocalFile(trackA);
var itemB = PlaybackQueueItem.FromLocalFile(trackB);

await mixer.LoadToDeckAsync(DeckId.A, itemA);
await mixer.LoadToDeckAsync(DeckId.B, itemB);
await mixer.PlayDeckAsync(DeckId.A);

var producer = GetOutputProducer(mixer);
var buffer = new byte[960 * 4];

var mixedDuringCrossfade = false;
await mixer.PlayDeckAsync(DeckId.B);

for (var i = 0; i < 200; i++)
{
    var read = producer.Read(buffer, 0, buffer.Length, out _);
    if (read <= 0)
        break;

    if (mixer.DeckA.CrossfadeGain > 0.05f && mixer.DeckB.CrossfadeGain > 0.05f)
        mixedDuringCrossfade = true;

    if (!mixer.CrossfadeInProgress)
        break;

    await Task.Delay(10);
}

if (!mixedDuringCrossfade)
{
    Console.Error.WriteLine("FAIL: both decks were not audible during crossfade window");
    return 1;
}

Console.WriteLine("OK: overlap crossfade mixed both decks");
return 0;

static IAudioPassiveProducer GetOutputProducer(AudioMixerService mixer)
{
    var field = typeof(AudioMixerService).GetField("_outputProducer",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    return (IAudioPassiveProducer)field.GetValue(mixer)!;
}
