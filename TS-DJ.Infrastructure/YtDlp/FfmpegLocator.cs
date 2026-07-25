namespace TS_DJ.Infrastructure.YtDlp;

public enum FfmpegLocationOrigin
{
    Bundled,
    PathEnvironment
}

public sealed class FfmpegLocationResult
{
    public required string Path { get; init; }
    public FfmpegLocationOrigin Origin { get; init; }
}

public sealed class FfmpegLocator
{
    private FfmpegLocationResult? _cached;

    public void InvalidateCache() => _cached = null;

    public FfmpegLocationResult? Locate()
    {
        if (_cached is not null && File.Exists(_cached.Path))
            return _cached;

        foreach (var candidate in GetBundledCandidates())
        {
            if (File.Exists(candidate))
            {
                _cached = new FfmpegLocationResult
                {
                    Path = candidate,
                    Origin = FfmpegLocationOrigin.Bundled
                };
                return _cached;
            }
        }

        var pathEnv = FindOnPath();
        if (pathEnv is not null)
        {
            _cached = new FfmpegLocationResult
            {
                Path = pathEnv,
                Origin = FfmpegLocationOrigin.PathEnvironment
            };
            return _cached;
        }

        _cached = null;
        return null;
    }

    /// <summary>
    /// Returns the directory or executable path suitable for yt-dlp's --ffmpeg-location.
    /// </summary>
    public string? GetYtDlpLocationArgument()
    {
        var located = Locate();
        if (located is null)
            return null;

        return Directory.Exists(located.Path) ? located.Path : Path.GetDirectoryName(located.Path);
    }

    private static IEnumerable<string> GetBundledCandidates()
    {
        var binaryName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var platformDir = BundledToolPaths.PlatformDirectory;

        foreach (var root in BundledToolPaths.GetAppSearchRoots())
        {
            yield return Path.Combine(root, "tools", "ffmpeg", platformDir, binaryName);
            yield return Path.Combine(root, "tools", "ffmpeg", binaryName);
            yield return Path.Combine(root, "tools", "ffmpeg", platformDir);
        }
    }

    private static string? FindOnPath()
    {
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVar))
            return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
