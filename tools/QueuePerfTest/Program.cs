using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.Infrastructure.Logging;
using TS_DJ.TeamSpeak.DependencyInjection;

var wavPath = args.Length > 0 ? args[0] : "/tmp/track1.wav";
var queueSize = args.Length > 1 && int.TryParse(args[1], out var parsedSize) ? parsedSize : 200;
var skipCount = args.Length > 2 && int.TryParse(args[2], out var parsedSkips) ? parsedSkips : 20;

if (!File.Exists(wavPath))
{
    Console.Error.WriteLine($"WAV not found: {wavPath}");
    return 1;
}

if (!TSLib.Audio.Opus.NativeMethods.PreloadLibrary())
{
    Console.Error.WriteLine("Failed to load libopus");
    return 1;
}

var tempDir = Path.Combine(Path.GetTempPath(), $"ts-dj-queue-perf-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDir);

try
{
    var tempPaths = new string[queueSize];
    for (var i = 0; i < queueSize; i++)
    {
        tempPaths[i] = Path.Combine(tempDir, $"track-{i:D4}.wav");
        File.Copy(wavPath, tempPaths[i], overwrite: true);
    }

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddTsDjInfrastructure();
    services.AddTsDjTeamSpeak();
    services.AddTsDjAudio();

    using var provider = services.BuildServiceProvider();
    var mixer = provider.GetRequiredService<IAudioMixerService>();
    var logService = provider.GetRequiredService<ILogService>();

    var items = tempPaths
        .Select((path, index) => PlaybackQueueItem.FromLocalFile(path, $"Track {index + 1}"))
        .ToList();

    mixer.EnqueueRange(items);
    mixer.PlayQueueItem(0);

    if (mixer.NowPlaying is null)
    {
        Console.Error.WriteLine("FAIL: could not start first queue item");
        return 1;
    }

    Console.WriteLine($"Queue size={queueSize}, measuring {skipCount} SkipNext transitions…");

    var timings = new List<long>(skipCount);
    for (var i = 0; i < skipCount; i++)
    {
        var sw = Stopwatch.StartNew();
        mixer.SkipNext();
        sw.Stop();
        timings.Add(sw.ElapsedMilliseconds);
        Console.WriteLine($"  Skip {i + 1}: {sw.ElapsedMilliseconds}ms (queued={mixer.Queue.Count(q => q.Status == PlaybackQueueStatus.Queued)})");
    }

    var first = timings[0];
    var last = timings[^1];
    var max = timings.Max();
    var ratio = first > 0 ? (double)max / first : 1.0;

    Console.WriteLine();
    Console.WriteLine($"First={first}ms, Last={last}ms, Max={max}ms, Max/First ratio={ratio:F2}");
    Console.WriteLine($"LogService entries={logService.Entries.Count} (cap={LogService.MaxLogEntries})");

    if (logService.Entries.Count > LogService.MaxLogEntries)
    {
        Console.Error.WriteLine("FAIL: log service exceeded cap");
        return 1;
    }

    if (ratio > 2.0)
    {
        Console.Error.WriteLine("FAIL: later SkipNext transitions exceeded 2× first transition time");
        return 1;
    }

    Console.WriteLine("QueuePerfTest passed.");
    return 0;
}
finally
{
    try
    {
        Directory.Delete(tempDir, recursive: true);
    }
    catch
    {
        // Best-effort cleanup.
    }
}
