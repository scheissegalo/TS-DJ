using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Audio.Mixing;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak.DependencyInjection;
using TSLib.Audio;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: SequentialQueueCrossfadeTest <track-a> <track-b> <track-c>");
    return 1;
}

foreach (var path in args.Take(3))
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
    Mode = PlaybackMode.SequentialQueue,
    CrossfadeEnabled = true,
    CrossfadeDurationSeconds = 1.0
});

foreach (var path in args.Take(3))
    mixer.Enqueue(PlaybackQueueItem.FromLocalFile(path));

mixer.PlayQueueItem(0);

var producer = GetOutputProducer(mixer);
var buffer = new byte[960 * 4];
var mixedDuringCrossfade = false;

for (var step = 0; step < 400; step++)
{
    producer.Read(buffer, 0, buffer.Length, out _);

    if (mixer.DeckA.CrossfadeGain > 0.05f && mixer.DeckB.CrossfadeGain > 0.05f)
        mixedDuringCrossfade = true;

    if (step == 50)
        mixer.SkipNext();

    if (step > 350)
        break;

    await Task.Delay(10);
}

if (!mixedDuringCrossfade)
{
    Console.Error.WriteLine("FAIL: both decks were not audible during sequential crossfade");
    return 1;
}

Console.WriteLine("OK: sequential queue crossfade mixed both decks");
return 0;

static IAudioPassiveProducer GetOutputProducer(AudioMixerService mixer)
{
    var field = typeof(AudioMixerService).GetField("_outputProducer",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    return (IAudioPassiveProducer)field.GetValue(mixer)!;
}
