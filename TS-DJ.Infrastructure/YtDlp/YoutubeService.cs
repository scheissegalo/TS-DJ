using System.Text.Json;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YoutubeService : IYoutubeService
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan PlaylistMetadataTimeout = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(180);

    private readonly YtDlpLocator _locator;
    private readonly YtDlpCommandBuilder _commandBuilder;
    private readonly YtDlpProcessRunner _processRunner;
    private readonly YtDlpDiagnostics _diagnostics;
    private readonly ILogger<YoutubeService> _logger;

    public YoutubeService(
        YtDlpLocator locator,
        YtDlpCommandBuilder commandBuilder,
        YtDlpProcessRunner processRunner,
        YtDlpDiagnostics diagnostics,
        ILogger<YoutubeService> logger)
    {
        _locator = locator;
        _commandBuilder = commandBuilder;
        _processRunner = processRunner;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public Task<YoutubeDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_diagnostics.Current);

    public Task RefreshDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        _diagnostics.RefreshAsync(cancellationToken);

    public async Task<YoutubeVideoMetadata> FetchVideoMetadataAsync(
        string videoUrl,
        CancellationToken cancellationToken = default)
    {
        var location = await RequireYtDlpAsync(cancellationToken);
        var args = await _commandBuilder.BuildVideoMetadataArgsAsync(videoUrl, cancellationToken);

        _logger.LogDebug(
            "Fetching YouTube metadata — videoId={VideoId}, command={Command}",
            TryGetVideoId(videoUrl),
            YtDlpProcessRunner.FormatCommandLine(location.Path, args));

        var output = await _processRunner.RunCaptureStdoutAsync(
            location.Path,
            args,
            MetadataTimeout,
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(title))
                throw new YtDlpException("Could not read video title from yt-dlp response.");

            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                id = TryGetVideoId(videoUrl);

            var uploader = root.TryGetProperty("uploader", out var uploaderEl) ? uploaderEl.GetString() : null;
            int? duration = root.TryGetProperty("duration", out var durationEl) && durationEl.TryGetDouble(out var seconds)
                ? (int)Math.Round(seconds)
                : null;
            var thumbnail = root.TryGetProperty("thumbnail", out var thumbEl) ? thumbEl.GetString() : null;
            var webpage = root.TryGetProperty("webpage_url", out var pageEl) ? pageEl.GetString() : videoUrl;

            return new YoutubeVideoMetadata
            {
                VideoId = id ?? string.Empty,
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

    public async Task<YoutubePlaylistMetadata> FetchPlaylistMetadataAsync(
        string inputUrl,
        CancellationToken cancellationToken = default)
    {
        if (!YoutubeUrlHelper.TryNormalizePlaylistUrl(inputUrl, out var normalizedPlaylistUrl))
            throw new YtDlpException("Invalid YouTube playlist URL.");

        var location = await RequireYtDlpAsync(cancellationToken);
        var args = await _commandBuilder.BuildFlatPlaylistArgsAsync(normalizedPlaylistUrl, cancellationToken);

        _logger.LogDebug(
            "Fetching YouTube playlist metadata — command={Command}",
            YtDlpProcessRunner.FormatCommandLine(location.Path, args));

        var output = await _processRunner.RunCaptureStdoutAsync(
            location.Path,
            args,
            PlaylistMetadataTimeout,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output) || output.Trim() == "null")
            throw new YtDlpException("Could not read YouTube playlist metadata.");

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(title))
                title = "YouTube Playlist";

            if (!root.TryGetProperty("entries", out var entriesEl) || entriesEl.ValueKind != JsonValueKind.Array)
                throw new YtDlpException("Playlist response did not contain entries.");

            var entries = new List<YoutubePlaylistEntry>();
            foreach (var entry in entriesEl.EnumerateArray())
            {
                var parsed = ParseFlatPlaylistEntry(entry);
                if (parsed is not null)
                    entries.Add(parsed);
            }

            if (entries.Count == 0)
                throw new YtDlpException("Playlist contains no playable videos.");

            _logger.LogInformation(
                "yt-dlp flat playlist '{Title}' parsed with {Count} entries",
                title.Trim(),
                entries.Count);

            return new YoutubePlaylistMetadata
            {
                Title = title.Trim(),
                PlaylistUrl = normalizedPlaylistUrl,
                Entries = entries
            };
        }
        catch (JsonException ex)
        {
            throw new YtDlpException("Failed to parse yt-dlp playlist response.", ex);
        }
    }

    public async Task<IPlaybackStreamHandle> ExtractAudioAsync(
        string videoUrl,
        CancellationToken cancellationToken = default)
    {
        var location = await RequireYtDlpAsync(cancellationToken);
        var videoId = TryGetVideoId(videoUrl) ?? videoUrl;
        var format = await _commandBuilder.GetAudioFormatSelectorAsync(cancellationToken);

        var tempDir = Path.Combine(Path.GetTempPath(), "ts-dj-ytdlp");
        Directory.CreateDirectory(tempDir);
        var tempMp3 = Path.Combine(tempDir, $"{Guid.NewGuid():N}.mp3");

        var args = await _commandBuilder.BuildExtractMp3ArgsAsync(videoUrl, tempMp3, cancellationToken);
        var commandLine = YtDlpProcessRunner.FormatCommandLine(location.Path, args);

        _logger.LogInformation(
            "Starting YouTube audio extraction — videoId={VideoId}, format={Format}, command={Command}",
            videoId,
            format,
            commandLine);

        try
        {
            await _processRunner.RunAsync(location.Path, args, StreamTimeout, cancellationToken);

            if (!File.Exists(tempMp3))
                throw new YtDlpException("yt-dlp did not produce an audio file.");

            var buffer = await File.ReadAllBytesAsync(tempMp3, cancellationToken);
            if (buffer.Length == 0)
                throw new YtDlpException("yt-dlp returned no audio data.");

            _diagnostics.LogPlaybackAttempt(videoId, format, commandLine, success: true);
            _logger.LogInformation(
                "YouTube audio extraction succeeded — videoId={VideoId}, bytes={Bytes}",
                videoId,
                buffer.Length);

            return new MemoryPlaybackStreamHandle(buffer);
        }
        catch (Exception ex)
        {
            _diagnostics.LogPlaybackAttempt(videoId, format, commandLine, success: false, failure: ex);
            throw;
        }
        finally
        {
            TryDeleteFile(tempMp3);
            var stem = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(tempMp3));
            foreach (var suffix in new[] { ".webm", ".m4a", ".opus", ".part", "" })
                TryDeleteFile(stem + suffix);
        }
    }

    private async Task<YtDlpLocationResult> RequireYtDlpAsync(CancellationToken cancellationToken)
    {
        return await _locator.LocateAsync(cancellationToken)
               ?? throw new YtDlpException("yt-dlp not found — configure path in Options.");
    }

    private static YoutubePlaylistEntry? ParseFlatPlaylistEntry(JsonElement entry)
    {
        var id = entry.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || id.Length != 11)
            return null;

        var url = entry.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(url) || !url.Contains("watch?v=", StringComparison.OrdinalIgnoreCase))
            url = YoutubeUrlHelper.BuildVideoUrl(id);

        var title = entry.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(title))
            title = "Unknown Title";

        var uploader = entry.TryGetProperty("uploader", out var uploaderEl) ? uploaderEl.GetString() : null;
        int? duration = entry.TryGetProperty("duration", out var durationEl) && durationEl.TryGetDouble(out var seconds)
            ? (int)Math.Round(seconds)
            : null;

        return new YoutubePlaylistEntry
        {
            VideoId = id,
            VideoUrl = YoutubeUrlHelper.TryNormalize(url, out var normalized) ? normalized : YoutubeUrlHelper.BuildVideoUrl(id),
            Title = title.Trim(),
            Uploader = string.IsNullOrWhiteSpace(uploader) ? null : uploader.Trim(),
            DurationSeconds = duration
        };
    }

    private static string? TryGetVideoId(string videoUrl)
    {
        if (!YoutubeUrlHelper.TryNormalize(videoUrl, out var normalized))
            return null;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return null;

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]), StringComparer.OrdinalIgnoreCase);

        return query.GetValueOrDefault("v");
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
}
