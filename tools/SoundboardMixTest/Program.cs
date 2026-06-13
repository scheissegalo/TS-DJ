using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak.DependencyInjection;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: SoundboardMixTest <music> <sfx1> <sfx2> [ts_host]");
    return 1;
}

var music = args[0];
var sfx1 = args[1];
var sfx2 = args[2];
var tsHost = args.Length > 3 ? args[3] : "127.0.0.1";

foreach (var f in new[] { music, sfx1, sfx2 })
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
    Address = tsHost,
    Nickname = "SoundboardMixTest",
    Channel = "/1"
});

var pass = true;

// Step 2: soundboard-only
Console.WriteLine("=== Step 2: Trigger soundboard (sound works) ===");
TriggerSfx(mixer, sfx1);
await Task.Delay(300);
if (!mixer.Soundboard.IsActive)
{
    Console.Error.WriteLine("FAIL step 2: soundboard not active");
    pass = false;
}
else
    Console.WriteLine("PASS step 2");

await WaitUntilAsync(() => !mixer.Soundboard.IsActive, TimeSpan.FromMinutes(2), "step 2 sfx EOF");

// Step 3: play music
Console.WriteLine("=== Step 3: Play music track ===");
await playback.LoadAsync(music);
await playback.PlayAsync();
await Task.Delay(500);
if (!mixer.Music.IsPlaying)
{
    Console.Error.WriteLine("FAIL step 3: music not playing");
    pass = false;
}
else
    Console.WriteLine("PASS step 3");

// Step 4: soundboard during music
Console.WriteLine("=== Step 4: Trigger soundboard during music ===");
TriggerSfx(mixer, sfx1);
TriggerSfx(mixer, sfx2);
await Task.Delay(300);
if (!mixer.Music.IsPlaying || !mixer.Soundboard.IsActive)
{
    Console.Error.WriteLine($"FAIL step 4: music={mixer.Music.IsPlaying}, sfx={mixer.Soundboard.IsActive}");
    pass = false;
}
else
    Console.WriteLine("PASS step 4");

await WaitUntilAsync(() => !mixer.Soundboard.IsActive, TimeSpan.FromMinutes(2), "step 4 sfx EOF");
if (!mixer.Music.IsPlaying)
{
    Console.Error.WriteLine("FAIL step 4: music stopped early");
    pass = false;
}

// Step 5: wait for music to end
Console.WriteLine("=== Step 5: Music ends ===");
await WaitUntilAsync(() => !mixer.Music.IsPlaying, TimeSpan.FromMinutes(5), "music EOF");
Console.WriteLine("Music finished");

// Step 6: soundboard AFTER music (critical regression test)
Console.WriteLine("=== Step 6: Trigger soundboard after music ended ===");
TriggerSfx(mixer, sfx1);
await Task.Delay(300);

if (!mixer.Soundboard.IsActive)
{
    Console.Error.WriteLine("FAIL step 6: soundboard not active after music ended");
    pass = false;
}
else
    Console.WriteLine("PASS step 6: soundboard active after music ended");

await WaitUntilAsync(() => !mixer.Soundboard.IsActive, TimeSpan.FromMinutes(2), "step 6 sfx EOF");

// Step 7: second music track
Console.WriteLine("=== Step 7: Play second music track ===");
await playback.LoadAsync(music);
await playback.PlayAsync();
await Task.Delay(500);

if (!mixer.Music.IsPlaying)
{
    Console.Error.WriteLine("FAIL step 7: second track not playing");
    pass = false;
}
else
    Console.WriteLine("PASS step 7");

TriggerSfx(mixer, sfx2);
await Task.Delay(300);
if (!mixer.Soundboard.IsActive)
{
    Console.Error.WriteLine("FAIL step 7: soundboard dead after second track start");
    pass = false;
}

await playback.StopAsync();
await teamSpeak.DisconnectAsync();

if (!pass)
{
    Console.Error.WriteLine("OVERALL FAIL");
    return 1;
}

Console.WriteLine("OVERALL PASS");
return 0;

static void TriggerSfx(IAudioMixerService mixer, string path)
{
    mixer.PlaySoundEffect(path);
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string description)
{
    var deadline = DateTime.UtcNow.Add(timeout);
    while (DateTime.UtcNow < deadline)
    {
        if (condition())
            return;

        await Task.Delay(100);
    }

    throw new TimeoutException($"Timed out waiting for: {description}");
}
