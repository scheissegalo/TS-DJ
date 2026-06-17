using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpMetadata
{
    public required string Title { get; init; }
    public string? Uploader { get; init; }
    public int? DurationSeconds { get; init; }
    public string? ThumbnailUrl { get; init; }
    public required string WebpageUrl { get; init; }
}

public sealed class YtDlpService
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(180);

    private readonly YtDlpLocator _locator;
    private readonly ILogger<YtDlpService> _logger;

    public YtDlpService(YtDlpLocator locator, ILogger<YtDlpService> logger)
    {
        _locator = locator;
        _logger = logger;
    }

    public async Task<YtDlpMetadata> FetchMetadataAsync(string videoUrl, CancellationToken cancellationToken = default)
    {
        var location = await _locator.LocateAsync(cancellationToken)
            ?? throw new YtDlpException("yt-dlp not found — configure path in Options.");

        var output = await RunProcessCaptureStdoutAsync(
            location.Path,
            ["--dump-json", "--no-playlist", "--no-warnings", "--", videoUrl],
            MetadataTimeout,
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(title))
                throw new YtDlpException("Could not read video title from yt-dlp response.");

            var uploader = root.TryGetProperty("uploader", out var uploaderEl) ? uploaderEl.GetString() : null;
            int? duration = root.TryGetProperty("duration", out var durationEl) && durationEl.TryGetDouble(out var seconds)
                ? (int)Math.Round(seconds)
                : null;
            var thumbnail = root.TryGetProperty("thumbnail", out var thumbEl) ? thumbEl.GetString() : null;
            var webpage = root.TryGetProperty("webpage_url", out var pageEl) ? pageEl.GetString() : videoUrl;

            return new YtDlpMetadata
            {
                Title = title.Trim(),
                Uploader = string.IsNullOrWhiteSpace(uploader) ? null : uploader.Trim(),
                DurationSeconds = duration,
                ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnail) ? null : thumbnail,
                WebpageUrl = YoutubeUrlHelper.TryNormalize(webpage ?? videoUrl, out var normalized)
                    ? normalized
                    : videoUrl
            };
        }
        catch (JsonException ex)
        {
            throw new YtDlpException("Failed to parse yt-dlp metadata response.", ex);
        }
    }

    public async Task<MemoryPlaybackStreamHandle> OpenMp3StreamAsync(
        string videoUrl,
        CancellationToken cancellationToken = default)
    {
        var location = await _locator.LocateAsync(cancellationToken)
            ?? throw new YtDlpException("yt-dlp not found — configure path in Options.");

        // yt-dlp cannot run FFmpeg audio extraction when writing to stdout (-o -); it streams
        // the raw download (WebM/Opus) instead. Extract to a temp file, then buffer MP3 in memory.
        var tempDir = Path.Combine(Path.GetTempPath(), "ts-dj-ytdlp");
        Directory.CreateDirectory(tempDir);
        var tempMp3 = Path.Combine(tempDir, $"{Guid.NewGuid():N}.mp3");

        try
        {
            await RunProcessAsync(
                location.Path,
                [
                    "-f", "bestaudio",
                    "-x", "--audio-format", "mp3",
                    "--audio-quality", "0",
                    "--postprocessor-args", "ffmpeg:-ar 44100 -ac 2",
                    "-o", tempMp3,
                    "--no-playlist",
                    "--no-warnings",
                    "--", videoUrl
                ],
                StreamTimeout,
                cancellationToken);

            if (!File.Exists(tempMp3))
                throw new YtDlpException("yt-dlp did not produce an audio file.");

            var buffer = await File.ReadAllBytesAsync(tempMp3, cancellationToken);
            if (buffer.Length == 0)
                throw new YtDlpException("yt-dlp returned no audio data.");

            _logger.LogInformation("yt-dlp produced {Bytes} bytes of MP3 audio for {Url}", buffer.Length, videoUrl);
            return new MemoryPlaybackStreamHandle(buffer);
        }
        finally
        {
            TryDeleteFile(tempMp3);
            var stem = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(tempMp3));
            foreach (var suffix in new[] { ".webm", ".m4a", ".opus", ".part", "" })
                TryDeleteFile(stem + suffix);
        }
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var location = await _locator.LocateAsync(cancellationToken);
        if (location is null)
            return null;

        try
        {
            return (await RunProcessCaptureStdoutAsync(
                location.Path,
                ["--version"],
                TimeSpan.FromSeconds(15),
                cancellationToken)).Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> RunProcessCaptureStdoutAsync(
        string executable,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var bytes = await RunProcessCaptureStdoutBytesAsync(executable, args, timeout, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private async Task RunProcessAsync(
        string executable,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        try
        {
            if (!process.Start())
                throw new YtDlpException("Failed to start yt-dlp process.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0)
                throw MapProcessFailure(stderr, process.ExitCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new YtDlpException("Network error resolving YouTube audio.");
        }
        catch (YtDlpException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            throw new YtDlpException("Network error resolving YouTube audio.", ex);
        }
    }

    private async Task<byte[]> RunProcessCaptureStdoutBytesAsync(
        string executable,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        try
        {
            if (!process.Start())
                throw new YtDlpException("Failed to start yt-dlp process.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var stdoutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cts.Token));

            var stderr = await stderrTask;
            var stdout = await stdoutTask;

            if (process.ExitCode != 0)
                throw MapProcessFailure(stderr, process.ExitCode);

            return stdout;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new YtDlpException("Network error resolving YouTube audio.");
        }
        catch (YtDlpException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            throw new YtDlpException("Network error resolving YouTube audio.", ex);
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    private static YtDlpException MapProcessFailure(string stderr, int exitCode)
    {
        var text = stderr.Trim();
        var lower = text.ToLowerInvariant();

        if (lower.Contains("private video") || lower.Contains("sign in") || lower.Contains("login"))
            return new YtDlpException("Video is private or requires sign-in.");

        if (lower.Contains("age") && lower.Contains("restrict"))
            return new YtDlpException("Video is age-restricted.");

        if (lower.Contains("unavailable") || lower.Contains("removed") || lower.Contains("not available"))
            return new YtDlpException("Video is unavailable.");

        if (string.IsNullOrWhiteSpace(text))
            return new YtDlpException($"yt-dlp failed with exit code {exitCode}.");

        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        return new YtDlpException(firstLine.Length > 200 ? firstLine[..200] : firstLine);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}

public sealed class MemoryPlaybackStreamHandle : IPlaybackStreamHandle
{
    private readonly byte[] _data;
    private bool _disposed;

    public MemoryPlaybackStreamHandle(byte[] data) => _data = data;

    public Stream OpenRead()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new MemoryStream(_data, writable: false);
    }

    public void Dispose() => _disposed = true;
}
