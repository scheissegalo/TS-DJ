using System.Net;

namespace TS_DJ.Audio.Decoding;

internal static class RemoteAudioHttp
{
    private const int MaxDownloadAttempts = 3;
    private const int MinUsableBytes = 4096;

    private static readonly HttpClient SharedClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TS-DJ");
        return client;
    }

    /// <summary>
    /// Downloads a remote audio resource into a seekable in-memory buffer.
    /// NAudio's MP3 reader requires seek support for ID3 tag parsing.
    /// Only the active track is buffered; nothing is persisted to disk.
    /// </summary>
    public static MemoryStream DownloadAsSeekableStream(string uri) =>
        DownloadAsSeekableStreamAsync(uri).GetAwaiter().GetResult();

    public static async Task<MemoryStream> DownloadAsSeekableStreamAsync(
        string uri,
        CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await DownloadOnceAsync(uri, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or HttpIOException or IOException or TaskCanceledException)
            {
                lastError = ex;
                if (attempt >= MaxDownloadAttempts)
                    break;

                await Task.Delay(250 * attempt, cancellationToken);
            }
        }

        throw new IOException("Failed to download remote audio stream after retries.", lastError);
    }

    private static async Task<MemoryStream> DownloadOnceAsync(string uri, CancellationToken cancellationToken)
    {
        using var response = await SharedClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Remote audio stream failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        var memory = new MemoryStream();
        await using (var network = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            try
            {
                await network.CopyToAsync(memory, cancellationToken);
            }
            catch (HttpIOException ex) when (IsUsableDespiteLengthMismatch(ex, memory))
            {
                // Navidrome transcodes may report a Content-Length slightly larger than
                // the actual MP3 output; the bytes received are usually complete.
            }
        }

        if (memory.Length < MinUsableBytes)
            throw new IOException("Remote audio stream returned insufficient data.");

        memory.Position = 0;
        return memory;
    }

    private static bool IsUsableDespiteLengthMismatch(HttpIOException ex, MemoryStream memory) =>
        ex.HttpRequestError == HttpRequestError.ResponseEnded && memory.Length >= MinUsableBytes;
}
