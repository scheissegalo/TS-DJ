using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Audio.Mixing;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: MediaPlaybackLoaderTest <local-audio-file>");
    return 1;
}

var path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 1;
}

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTsDjInfrastructure();
services.AddTsDjAudio();

await using var provider = services.BuildServiceProvider();
var loader = provider.GetRequiredService<IMediaPlaybackLoader>();

var item = PlaybackQueueItem.FromLocalFile(path);
var result = await loader.LoadAsync(item);

if (result.Kind != MediaLoadKind.LocalFile || result.LocalFilePath != path)
{
    Console.Error.WriteLine($"FAIL: expected local file result, got {result.Kind}");
    return 1;
}

Console.WriteLine($"OK: loaded {result.LocalFilePath}");
return 0;
