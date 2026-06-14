using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Audio.Mixing;
using TS_DJ.Audio.Mixing.Sources;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak.DependencyInjection;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SoundboardSaturationTest <sfx-file> [burst-count]");
    return 1;
}

var sfxPath = Path.GetFullPath(args[0]);
if (!File.Exists(sfxPath))
{
    Console.Error.WriteLine($"Missing: {sfxPath}");
    return 1;
}

var burstCount = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 120;

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddTsDjInfrastructure();
services.AddTsDjTeamSpeak();
services.AddTsDjAudio();

using var provider = services.BuildServiceProvider();
var mixer = provider.GetRequiredService<IAudioMixerService>();
var mixerImpl = (AudioMixerService)mixer;
var soundboard = mixerImpl.Soundboard;

Console.WriteLine($"=== Preload clip: {sfxPath} ===");
var clip = TS_DJ.Audio.Mixing.PreloadedClip.TryLoad(sfxPath);
if (clip is null)
{
    Console.Error.WriteLine("FAIL: could not preload clip (too long or unsupported)");
    return 1;
}

mixerImpl.ClipCache.Set(sfxPath, clip);
Console.WriteLine($"Preloaded {clip.SampleCount} samples");

Console.WriteLine($"=== Burst {burstCount} rapid triggers (expect limit drops) ===");
var tasks = Enumerable.Range(0, burstCount)
    .Select(_ => Task.Run(() =>
    {
        try
        {
            mixer.PlaySoundEffect(sfxPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PlaySoundEffect error: {ex.Message}");
        }
    }))
    .ToArray();

await Task.WhenAll(tasks);
Console.WriteLine($"Burst complete — live={soundboard.ActiveEffectCount}, active={soundboard.IsActive}");

Console.WriteLine("=== Drain lifecycle until idle ===");
var drained = await DrainSoundboardAsync(soundboard, TimeSpan.FromSeconds(30));
if (!drained)
{
    Console.Error.WriteLine($"FAIL: soundboard stuck live={soundboard.ActiveEffectCount} after saturation");
    return 1;
}

Console.WriteLine("PASS: soundboard returned to idle after saturation");

Console.WriteLine("=== Recovery play after saturation ===");
mixer.PlaySoundEffect(sfxPath);
await Task.Delay(100);

if (soundboard.ActiveEffectCount == 0)
{
    Console.Error.WriteLine("FAIL: recovery play did not start any live effects");
    return 1;
}

Console.WriteLine($"Recovery play started (live={soundboard.ActiveEffectCount})");

var recoveryDrained = await DrainSoundboardAsync(soundboard, TimeSpan.FromSeconds(15));
if (!recoveryDrained)
{
    Console.Error.WriteLine("FAIL: recovery effect did not finish");
    return 1;
}

Console.WriteLine("=== Verify music path still usable ===");
mixer.Enqueue(sfxPath);
if (mixer.Queue.Count == 0)
{
    Console.Error.WriteLine("FAIL: queue enqueue broken after soundboard stress");
    return 1;
}

mixer.ClearQueue();
Console.WriteLine("OVERALL PASS");
return 0;

static async Task<bool> DrainSoundboardAsync(SoundEffectSource soundboard, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow.Add(timeout);
    while (DateTime.UtcNow < deadline)
    {
        soundboard.TickCleanup(0);
        if (!soundboard.IsActive)
            return true;

        await Task.Delay(20);
    }

    return !soundboard.IsActive;
}
