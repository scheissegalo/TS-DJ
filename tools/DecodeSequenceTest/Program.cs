using TS_DJ.Audio.Decoding;

var wav = args.Length > 0 ? args[0] : "/media/berni/FatExt2TB/clones/bark/woman_moan.wav";
var mp3 = args.Length > 1 ? args[1] : "/media/berni/FatExt2TB/clones/bark/elementosAudio.mp3";

if (!File.Exists(wav) || !File.Exists(mp3))
{
    Console.Error.WriteLine("Test files not found");
    return 1;
}

using (var decoder = new AudioFileDecoder())
{
    decoder.Open(wav);
    var buffer = new float[48_000 * 2];
    while (decoder.Output.Read(buffer, 0, buffer.Length) > 0)
    {
    }

    Console.WriteLine($"WAV EOF pending={decoder.ConsumeEofPending()}");
}

using (var decoder = new AudioFileDecoder())
{
    decoder.Open(mp3);
    var buffer = new float[48_000 * 2];
    var first = decoder.Output.Read(buffer, 0, buffer.Length);
    Console.WriteLine($"MP3 first read after WAV sequence: samples={first}");
    return first > 0 ? 0 : 1;
}
