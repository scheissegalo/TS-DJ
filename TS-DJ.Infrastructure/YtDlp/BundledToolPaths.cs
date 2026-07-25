namespace TS_DJ.Infrastructure.YtDlp;

internal static class BundledToolPaths
{
    internal static string PlatformDirectory =>
        OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    internal static IEnumerable<string> GetAppSearchRoots()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return baseDir;
        yield return Path.GetFullPath(Path.Combine(baseDir, ".."));
        yield return Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
    }
}
