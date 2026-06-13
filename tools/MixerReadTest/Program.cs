using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TS_DJ.Audio.Mixing.Sources;
using TS_DJ.Core.Audio;

var mp3a = args[0];
var mp3b = args[1];
var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<MusicTrackSource>();
var format = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);
var sync = new object();
var mixer = new MixingSampleProvider(format) { ReadFully = false };
var music = new MusicTrackSource("music", "Music", mixer, sync, logger);

music.Open(mp3a);
music.Play();
ConsumeEof(music, mixer, sync);

lock (sync)
{
    music.Open(mp3b);
    music.Play();
}

var floatBuf = new float[48_000 * 2];
int samples;
lock (sync)
    samples = mixer.Read(floatBuf, 0, floatBuf.Length);

Console.WriteLine($"MP3->MP3 mixer read: samples={samples}, IsPlaying={music.IsPlaying}");
return samples > 0 ? 0 : 1;

static void ConsumeEof(MusicTrackSource music, MixingSampleProvider mixer, object sync)
{
    var wave = mixer.ToWaveProvider16();
    var buf = new byte[960 * 4];
    while (true)
    {
        lock (sync)
        {
            wave.Read(buf, 0, buf.Length);
            var method = typeof(MusicTrackSource).GetMethod("TryConsumeTrackEnd",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            if ((bool)method.Invoke(music, [null])!)
                return;
        }
    }
}
