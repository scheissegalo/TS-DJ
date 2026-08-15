using TS_DJ.Core.Models;

namespace TS_DJ.Infrastructure.YtDlp;

public enum JsRuntimeOrigin
{
    Configured,
    Bundled,
    PathEnvironment
}

public sealed class JsRuntimeCandidate
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public JsRuntimeOrigin Origin { get; init; }
    public bool IsAvailable { get; init; }
}

public sealed class JsRuntimeDetection
{
    public string? SelectedRuntime { get; init; }
    public string? SelectedPath { get; init; }
    public JsRuntimeOrigin? SelectedOrigin { get; init; }

    /// <summary>
    /// When a custom <c>JsRuntimePath</c> is configured, the runtime kind detected from the
    /// binary itself (deno/node/bun/quickjs) — used to correct name/path mismatches.
    /// </summary>
    public string? ConfiguredPathRuntimeKind { get; init; }

    public required IReadOnlyList<JsRuntimeCandidate> Candidates { get; init; }

    public string StatusSummary
    {
        get
        {
            if (SelectedRuntime is not null && SelectedPath is not null)
                return $"{SelectedRuntime} ({SelectedOrigin}): {SelectedPath}";

            var available = Candidates.Where(c => c.IsAvailable).ToList();
            if (available.Count == 0)
                return "No JS runtime detected (bundled or PATH)";

            return $"None selected — available: {string.Join(", ", available.Select(c => c.Name))}";
        }
    }
}

public sealed class JsRuntimeLocator
{
    private static readonly (string Name, string[] BinaryNames)[] RuntimeDefinitions =
    [
        ("deno", ["deno"]),
        ("quickjs", ["qjs", "quickjs"]),
        ("node", ["node"]),
        ("bun", ["bun"])
    ];

    private JsRuntimeDetection? _cached;

    public void InvalidateCache() => _cached = null;

    public async Task<JsRuntimeDetection> DetectAsync(
        YtDlpSettings settings,
        YtDlpProcessRunner processRunner,
        CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
            return _cached;

        var candidates = new List<JsRuntimeCandidate>();
        foreach (var (name, binaryNames) in RuntimeDefinitions)
        {
            foreach (var candidate in await FindCandidatesAsync(name, binaryNames, settings, processRunner, cancellationToken))
                candidates.Add(candidate);
        }

        var selected = SelectRuntime(settings, candidates);
        var configuredPathRuntimeKind = await DetectConfiguredPathRuntimeKindAsync(settings, processRunner, cancellationToken);

        _cached = new JsRuntimeDetection
        {
            SelectedRuntime = selected?.Name,
            SelectedPath = selected?.Path,
            SelectedOrigin = selected?.Origin,
            ConfiguredPathRuntimeKind = configuredPathRuntimeKind,
            Candidates = candidates
        };

        return _cached;
    }

    public IReadOnlyList<string> BuildJsRuntimeArgs(YtDlpSettings settings, JsRuntimeDetection detection)
    {
        if (settings.JsRuntime == YoutubeJsRuntimePreference.None)
            return ["--no-js-runtimes"];

        var selected = ResolveSelectedCandidate(settings, detection);
        if (selected is null)
            return [];

        var runtimeName = selected.Name;
        if (!string.IsNullOrWhiteSpace(selected.Path) && !string.IsNullOrWhiteSpace(detection.ConfiguredPathRuntimeKind))
            runtimeName = detection.ConfiguredPathRuntimeKind!;

        var runtimeArg = string.IsNullOrWhiteSpace(selected.Path)
            ? runtimeName
            : $"{runtimeName}:{selected.Path}";

        var args = new List<string>();
        if (ShouldClearDefaultRuntimes(settings, detection, selected))
            args.Add("--no-js-runtimes");

        args.Add("--js-runtimes");
        args.Add(runtimeArg);
        return args;
    }

    private static bool ShouldClearDefaultRuntimes(
        YtDlpSettings settings,
        JsRuntimeDetection detection,
        JsRuntimeCandidate selected)
    {
        if (settings.JsRuntime is YoutubeJsRuntimePreference.Auto or YoutubeJsRuntimePreference.Deno)
            return false;

        var denoAvailable = detection.Candidates.Any(c =>
            c.IsAvailable && c.Name.Equals("deno", StringComparison.OrdinalIgnoreCase));

        return denoAvailable
               && !selected.Name.Equals("deno", StringComparison.OrdinalIgnoreCase);
    }

    private static JsRuntimeCandidate? ResolveSelectedCandidate(YtDlpSettings settings, JsRuntimeDetection detection)
    {
        if (settings.JsRuntime == YoutubeJsRuntimePreference.None)
            return null;

        if (!string.IsNullOrWhiteSpace(settings.JsRuntimePath))
        {
            var path = Path.GetFullPath(settings.JsRuntimePath.Trim());
            var runtimeName = MapPreferenceToRuntimeName(settings.JsRuntime);
            return new JsRuntimeCandidate
            {
                Name = runtimeName,
                Path = path,
                Origin = JsRuntimeOrigin.Configured,
                IsAvailable = File.Exists(path) || Directory.Exists(path)
            };
        }

        if (settings.JsRuntime != YoutubeJsRuntimePreference.Auto)
        {
            var runtimeName = MapPreferenceToRuntimeName(settings.JsRuntime);
            return detection.Candidates.FirstOrDefault(c =>
                c.IsAvailable && c.Name.Equals(runtimeName, StringComparison.OrdinalIgnoreCase));
        }

        return detection.Candidates.FirstOrDefault(c => c.IsAvailable);
    }

    private static JsRuntimeCandidate? SelectRuntime(YtDlpSettings settings, IReadOnlyList<JsRuntimeCandidate> candidates)
    {
        if (settings.JsRuntime == YoutubeJsRuntimePreference.None)
            return null;

        if (!string.IsNullOrWhiteSpace(settings.JsRuntimePath))
        {
            var path = Path.GetFullPath(settings.JsRuntimePath.Trim());
            return new JsRuntimeCandidate
            {
                Name = MapPreferenceToRuntimeName(settings.JsRuntime),
                Path = path,
                Origin = JsRuntimeOrigin.Configured,
                IsAvailable = File.Exists(path) || Directory.Exists(path)
            };
        }

        if (settings.JsRuntime != YoutubeJsRuntimePreference.Auto)
        {
            var runtimeName = MapPreferenceToRuntimeName(settings.JsRuntime);
            return candidates.FirstOrDefault(c =>
                c.IsAvailable && c.Name.Equals(runtimeName, StringComparison.OrdinalIgnoreCase));
        }

        return candidates.FirstOrDefault(c => c.IsAvailable);
    }

    private static async Task<IReadOnlyList<JsRuntimeCandidate>> FindCandidatesAsync(
        string runtimeName,
        string[] binaryNames,
        YtDlpSettings settings,
        YtDlpProcessRunner processRunner,
        CancellationToken cancellationToken)
    {
        var results = new List<JsRuntimeCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bundled in GetBundledCandidates(runtimeName, binaryNames))
        {
            if (!seen.Add(bundled))
                continue;

            results.Add(new JsRuntimeCandidate
            {
                Name = runtimeName,
                Path = bundled,
                Origin = JsRuntimeOrigin.Bundled,
                IsAvailable = await ValidateJsRuntimeAsync(bundled, processRunner, cancellationToken)
            });
        }

        foreach (var binaryName in binaryNames)
        {
            foreach (var pathCandidate in GetPathCandidates(binaryName))
            {
                if (!seen.Add(pathCandidate))
                    continue;

                results.Add(new JsRuntimeCandidate
                {
                    Name = runtimeName,
                    Path = pathCandidate,
                    Origin = JsRuntimeOrigin.PathEnvironment,
                    IsAvailable = await ValidateJsRuntimeAsync(pathCandidate, processRunner, cancellationToken)
                });
            }
        }

        return results;
    }

    private static IEnumerable<string> GetBundledCandidates(string runtimeName, string[] binaryNames)
    {
        var platformDir = BundledToolPaths.PlatformDirectory;

        foreach (var root in BundledToolPaths.GetAppSearchRoots())
        {
            var searchDirs = new[]
            {
                Path.Combine(root, "tools", "js-runtimes", runtimeName, platformDir),
                Path.Combine(root, "tools", "js-runtimes", runtimeName)
            };

            foreach (var searchDir in searchDirs)
            {
                if (!Directory.Exists(searchDir))
                    continue;

                foreach (var binaryName in binaryNames)
                {
                    var direct = Path.Combine(searchDir, binaryName);
                    if (File.Exists(direct))
                        yield return direct;

                    if (OperatingSystem.IsWindows())
                    {
                        var withExe = direct + ".exe";
                        if (File.Exists(withExe))
                            yield return withExe;
                    }
                }
            }

            foreach (var binaryName in binaryNames)
            {
                var legacyNames = OperatingSystem.IsWindows()
                    ? new[] { $"{binaryName}-windows-x86_64.exe", binaryName, $"{binaryName}.exe" }
                    : new[] { $"{binaryName}-linux-x86_64", binaryName };

                foreach (var legacyName in legacyNames)
                {
                    var legacy = Path.Combine(root, "tools", "js-runtimes", legacyName);
                    if (File.Exists(legacy))
                        yield return legacy;
                }
            }
        }
    }

    private static IEnumerable<string> GetPathCandidates(string binaryName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVar))
            yield break;

        var names = OperatingSystem.IsWindows()
            ? new[] { binaryName + ".exe", binaryName }
            : new[] { binaryName };

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }
    }

    private static async Task<bool> ValidateJsRuntimeAsync(
        string path,
        YtDlpProcessRunner processRunner,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            await processRunner.RunCaptureStdoutAsync(path, ["--version"], TimeSpan.FromSeconds(10), cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> DetectConfiguredPathRuntimeKindAsync(
        YtDlpSettings settings,
        YtDlpProcessRunner processRunner,
        CancellationToken cancellationToken)
    {
        if (settings.JsRuntime == YoutubeJsRuntimePreference.None)
            return null;

        if (string.IsNullOrWhiteSpace(settings.JsRuntimePath))
            return null;

        var path = Path.GetFullPath(settings.JsRuntimePath.Trim());
        if (!File.Exists(path))
            return null;

        try
        {
            var output = (await processRunner.RunCaptureStdoutAsync(
                path,
                ["--version"],
                TimeSpan.FromSeconds(10),
                cancellationToken)).Trim();

            if (string.IsNullOrWhiteSpace(output))
                return null;

            var lower = output.ToLowerInvariant();
            if (lower.Contains("deno"))
                return "deno";
            if (lower.Contains("bun"))
                return "bun";
            if (output.Length > 1 && output[0] is 'v' or 'V' && char.IsDigit(output[1]))
                return "node";
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string MapPreferenceToRuntimeName(YoutubeJsRuntimePreference preference) =>
        preference switch
        {
            YoutubeJsRuntimePreference.Deno => "deno",
            YoutubeJsRuntimePreference.QuickJs => "quickjs",
            YoutubeJsRuntimePreference.Node => "node",
            YoutubeJsRuntimePreference.Bun => "bun",
            _ => "deno"
        };
}
