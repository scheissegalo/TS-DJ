using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Audio.Media;
using TS_DJ.Audio.Mixing;
using TS_DJ.Audio.Playback;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak;
using TS_DJ.TeamSpeak.DependencyInjection;
using TSLib.Audio;
using TSLib.Helper;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: AsyncAdvanceCrossfadeTest <track-a> <track-b>");
    return 1;
}

foreach (var path in args.Take(2))
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
services.AddSingleton<MediaPlaybackLoader>();
services.AddSingleton<IMediaPlaybackLoader>(sp =>
    new SlowMediaPlaybackLoader(sp.GetRequiredService<MediaPlaybackLoader>(), TimeSpan.FromMilliseconds(300)));
services.AddSingleton<MediaLoadTimingTracker>();
services.AddSingleton<MediaLoadPrefetchCache>(sp =>
    new MediaLoadPrefetchCache(
        sp.GetRequiredService<IMediaPlaybackLoader>(),
        sp.GetService<ILogger<MediaLoadPrefetchCache>>()));
services.AddSingleton<AudioMixerService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AudioMixerService>>();
    var musicLogger = sp.GetRequiredService<ILogger<TS_DJ.Audio.Mixing.Sources.MusicTrackSource>>();
    var soundboardLogger = sp.GetRequiredService<ILogger<TS_DJ.Audio.Mixing.Sources.SoundEffectSource>>();
    var outputLogger = sp.GetRequiredService<ILogger<MixerOutputProducer>>();
    var teamSpeak = sp.GetRequiredService<TeamSpeakService>();
    return new AudioMixerService(
        logger,
        musicLogger,
        soundboardLogger,
        outputLogger,
        teamSpeak,
        new Id(2),
        sp.GetRequiredService<IMediaPlaybackLoader>(),
        sp.GetService<IMediaSourceRegistry>(),
        sp.GetService<MediaLoadPrefetchCache>(),
        sp.GetService<MediaLoadTimingTracker>());
});
services.AddSingleton<IAudioMixerService>(sp => sp.GetRequiredService<AudioMixerService>());

await using var provider = services.BuildServiceProvider();
var mixer = (AudioMixerService)provider.GetRequiredService<IAudioMixerService>();

mixer.SetPlaybackSettings(new PlaybackSettings
{
    Mode = PlaybackMode.SequentialQueue,
    CrossfadeEnabled = true,
    CrossfadeDurationSeconds = 1.0
});

mixer.Enqueue(PlaybackQueueItem.FromLocalFile(args[0]));
mixer.Enqueue(PlaybackQueueItem.FromLocalFile(args[1]));
mixer.PlayQueueItem(0);

var producer = GetOutputProducer(mixer);
var buffer = new byte[960 * 4];

for (var i = 0; i < 30; i++)
{
    producer.Read(buffer, 0, buffer.Length, out _);
    await Task.Delay(10);
}

mixer.SkipNext();

var nonZeroDuringAdvance = 0;
var sawAdvancePending = false;

for (var i = 0; i < 80; i++)
{
    var read = producer.Read(buffer, 0, buffer.Length, out _);
    if (mixer.AdvancePending)
    {
        sawAdvancePending = true;
        if (read > 0)
            nonZeroDuringAdvance++;
    }

    await Task.Delay(10);
}

if (!sawAdvancePending)
{
    Console.Error.WriteLine("FAIL: advance pending was never observed after skip");
    return 1;
}

if (nonZeroDuringAdvance == 0)
{
    Console.Error.WriteLine("FAIL: mixer produced silence while advance was pending (lock blocked during load)");
    return 1;
}

Console.WriteLine($"OK: mixer kept outputting during async advance ({nonZeroDuringAdvance} non-zero reads)");
return 0;

static IAudioPassiveProducer GetOutputProducer(AudioMixerService mixer)
{
    var field = typeof(AudioMixerService).GetField("_outputProducer",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    return (IAudioPassiveProducer)field.GetValue(mixer)!;
}

internal sealed class SlowMediaPlaybackLoader : IMediaPlaybackLoader
{
    private readonly IMediaPlaybackLoader _inner;
    private readonly TimeSpan _delay;
    private int _loadCount;

    public SlowMediaPlaybackLoader(IMediaPlaybackLoader inner, TimeSpan delay)
    {
        _inner = inner;
        _delay = delay;
    }

    public async Task<MediaLoadResult> LoadAsync(
        PlaybackQueueItem item,
        IPlaybackStreamHandle? prefetchedStream = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _loadCount) > 1)
            await Task.Delay(_delay, cancellationToken);

        return await _inner.LoadAsync(item, prefetchedStream, cancellationToken);
    }
}
