using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Decoding;
using TS_DJ.Audio.DependencyInjection;
using TS_DJ.Audio.Mixing;
using TS_DJ.Core.Audio;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.Infrastructure.DependencyInjection;
using TS_DJ.TeamSpeak.DependencyInjection;
using TSLib.Audio;

var validMp3 = args.Length > 0 ? args[0] : "/tmp/ts-dj-test.mp3";

if (!File.Exists(validMp3))
{
    Console.Error.WriteLine($"Valid MP3 not found: {validMp3}");
    Console.Error.WriteLine("Usage: DecoderFailureTest <valid-mp3>");
    return 1;
}

if (!TSLib.Audio.Opus.NativeMethods.PreloadLibrary())
{
    Console.Error.WriteLine("Failed to load libopus");
    return 1;
}

var invalidOpenPath = Path.Combine(Path.GetTempPath(), "ts-dj-invalid-open.mp3");
File.WriteAllBytes(invalidOpenPath, [0x00, 0x01, 0x02, 0x03]);

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddTsDjInfrastructure();
services.AddTsDjTeamSpeak();
services.AddTsDjAudio();

using var provider = services.BuildServiceProvider();

Console.WriteLine("=== Test 1: injected exception — mixer read survives ===");
if (!RunInjectedExceptionTest())
    return 1;

Console.WriteLine("=== Test 2: EofTrackingSampleProvider catches decode exception ===");
if (!RunEofTrackingDecodeFailureTest(validMp3))
    return 1;

Console.WriteLine("=== Test 3: open failure queue skip ===");
if (!RunOpenFailureQueueTest(provider, validMp3, invalidOpenPath))
    return 1;

Console.WriteLine("=== Test 4: mid-decode failure queue recovery ===");
if (!RunDecodeFailureQueueTest(provider, validMp3))
    return 1;

Console.WriteLine("DecoderFailureTest passed — mixer and pipeline survived all failures.");
return 0;

static bool RunInjectedExceptionTest()
{
    var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
    var sync = new object();
    var innerMixer = new MixingSampleProvider(format) { ReadFully = false };
    var musicLogger = LoggerFactory.Create(_ => { }).CreateLogger<TS_DJ.Audio.Mixing.Sources.MusicTrackSource>();
    var music = new TS_DJ.Audio.Mixing.Sources.MusicTrackSource("test", "Test", innerMixer, sync, musicLogger);

    InjectSource(music, new ThrowingSampleProvider(new IdleSampleProvider(format), throwsAfterReads: 10));
    SetMusicPlaying(music);

    var producer = new MixerOutputProducer(
        innerMixer,
        sync,
        () => music.IsPlaying,
        () => { },
        onReadException: _ => { });

    var buf = new byte[960 * 4];
    try
    {
        for (var i = 0; i < 500; i++)
            producer.Read(buf, 0, buf.Length, out _);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL test 1: exception escaped: {ex.GetType().Name}");
        return false;
    }

    Console.WriteLine("  500 mixer reads completed without exception escape");
    return true;
}

static bool RunEofTrackingDecodeFailureTest(string validMp3)
{
    var decoder = new AudioFileDecoder();
    decoder.Open(validMp3);
    WrapResampledWithThrowingProvider(decoder);

    var buffer = new float[48_000 * 2];
    Exception? escaped = null;
    try
    {
        for (var i = 0; i < 100; i++)
            decoder.Output.Read(buffer, 0, buffer.Length);
    }
    catch (Exception ex)
    {
        escaped = ex;
    }

    if (escaped is not null)
    {
        Console.Error.WriteLine($"FAIL test 2: exception escaped EofTrackingSampleProvider: {escaped.GetType().Name}");
        return false;
    }

    if (!decoder.ConsumeDecodeFailurePending(out var error))
    {
        Console.Error.WriteLine("FAIL test 2: decode failure pending was not signaled");
        return false;
    }

    Console.WriteLine($"  Decode failure isolated at source boundary ({error?.GetType().Name})");
    return true;
}

static bool RunOpenFailureQueueTest(IServiceProvider provider, string validMp3, string invalidPath)
{
    var mixer = CreateFreshMixer(provider);
    var output = GetOutputProducer(mixer);

    mixer.Enqueue(validMp3);
    mixer.Enqueue(invalidPath);
    mixer.Enqueue(validMp3);
    mixer.Start();

    return DriveQueueUntil(
        output,
        () =>
        {
            var items = mixer.Queue.ToList();
            return items.Count == 3
                && items[0].Status == PlaybackQueueStatus.Played
                && items[1].Status == PlaybackQueueStatus.Failed
                && items[2].Status == PlaybackQueueStatus.Playing
                && mixer.Music.IsPlaying;
        },
        "open failure skip",
        () => DumpQueue(mixer));
}

static bool RunDecodeFailureQueueTest(IServiceProvider provider, string validMp3)
{
    var mixer = CreateFreshMixer(provider);
    var output = GetOutputProducer(mixer);
    var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
    var injected = false;

    mixer.Enqueue(validMp3);
    mixer.Enqueue(validMp3);
    mixer.Enqueue(validMp3);
    mixer.Start();

    return DriveQueueUntil(
        output,
        () =>
        {
            if (!injected
                && mixer.Queue.Count(i => i.Status == PlaybackQueueStatus.Played) == 1
                && mixer.NowPlaying is not null
                && mixer.Music.IsPlaying)
            {
                InjectSource(
                    mixer.Music,
                    new ThrowingSampleProvider(new IdleSampleProvider(format), throwsAfterReads: 0));
                injected = true;
                Console.WriteLine("  Injected mid-decode failure on track 2");
            }

            var items = mixer.Queue.ToList();
            return injected
                && items[0].Status == PlaybackQueueStatus.Played
                && items[1].Status == PlaybackQueueStatus.Failed
                && items[2].Status == PlaybackQueueStatus.Playing
                && mixer.Music.IsPlaying;
        },
        "decode failure queue recovery",
        () => DumpQueue(mixer));
}

static bool DriveQueueUntil(
    IAudioPassiveProducer output,
    Func<bool> success,
    string label,
    Action dumpOnFailure)
{
    var buf = new byte[960 * 4];
    var deadline = DateTime.UtcNow.AddSeconds(30);

    try
    {
        while (DateTime.UtcNow < deadline)
        {
            output.Read(buf, 0, buf.Length, out _);
            if (success())
            {
                Console.WriteLine($"  {label}: recovery confirmed");
                return true;
            }

            Thread.Sleep(5);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {label}: exception escaped read loop: {ex.GetType().Name}");
        return false;
    }

    dumpOnFailure();
    Console.Error.WriteLine($"FAIL {label}: expected recovery state not reached");
    return false;
}

static AudioMixerService CreateFreshMixer(IServiceProvider provider) =>
    new(
        provider.GetRequiredService<ILogger<AudioMixerService>>(),
        provider.GetRequiredService<ILogger<TS_DJ.Audio.Mixing.Sources.MusicTrackSource>>(),
        provider.GetRequiredService<ILogger<TS_DJ.Audio.Mixing.Sources.SoundEffectSource>>(),
        provider.GetRequiredService<ILogger<MixerOutputProducer>>(),
        provider.GetRequiredService<TS_DJ.TeamSpeak.TeamSpeakService>(),
        new TSLib.Helper.Id(99),
        provider.GetRequiredService<IMediaPlaybackLoader>());

static void DumpQueue(AudioMixerService mixer)
{
    foreach (var item in mixer.Queue)
        Console.Error.WriteLine($"  Queue: {item.DisplayName} [{item.Status}]");
    Console.Error.WriteLine($"  NowPlaying: {mixer.NowPlaying?.FilePath ?? "null"}, IsPlaying={mixer.Music.IsPlaying}");
}

static void WrapResampledWithThrowingProvider(AudioFileDecoder decoder)
{
    var outputField = typeof(AudioFileDecoder).GetField("_output", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var output = outputField.GetValue(decoder)!;
    var resampledField = output.GetType().GetField("_resampled", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var resampled = (ISampleProvider)resampledField.GetValue(output)!;
    resampledField.SetValue(output, new ThrowingSampleProvider(resampled, throwsAfterReads: 5));
}

static IAudioPassiveProducer GetOutputProducer(AudioMixerService mixer)
{
    var field = typeof(AudioMixerService).GetField("_outputProducer", BindingFlags.Instance | BindingFlags.NonPublic)!;
    return (IAudioPassiveProducer)field.GetValue(mixer)!;
}

static void SetMusicPlaying(TS_DJ.Audio.Mixing.Sources.MusicTrackSource music)
{
    typeof(TS_DJ.Audio.Mixing.Sources.MusicTrackSource)
        .GetField("_trackActive", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(music, true);

    var gated = typeof(TS_DJ.Audio.Mixing.Sources.MusicTrackSource)
        .GetField("_gated", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(music)!;
    gated.GetType().GetProperty("IsPlaying")!.SetValue(gated, true);
}

static void InjectSource(TS_DJ.Audio.Mixing.Sources.MusicTrackSource music, ISampleProvider source)
{
    var switchable = typeof(TS_DJ.Audio.Mixing.Sources.MusicTrackSource)
        .GetField("_switchable", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(music)!;
    switchable.GetType().GetMethod("SetSource")!.Invoke(switchable, [source]);
}

internal sealed class ThrowingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _inner;
    private readonly int _throwsAfterReads;
    private int _reads;

    public ThrowingSampleProvider(ISampleProvider inner, int throwsAfterReads)
    {
        _inner = inner;
        _throwsAfterReads = throwsAfterReads;
        WaveFormat = inner.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        _reads++;
        if (_reads > _throwsAfterReads)
            throw new IndexOutOfRangeException("Simulated NLayer LayerIIIDecoder failure");

        return _inner.Read(buffer, offset, count);
    }
}

internal sealed class IdleSampleProvider : ISampleProvider
{
    public IdleSampleProvider(WaveFormat format) => WaveFormat = format;
    public WaveFormat WaveFormat { get; }
    public int Read(float[] buffer, int offset, int count) => 0;
}
