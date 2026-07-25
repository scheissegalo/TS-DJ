using System.Reflection;

namespace TS_DJ.Core;

public static class AppVersion
{
    public static Version Current { get; } = ParseVersion(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString()
        ?? "0.0.0");

    public static string Display { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Current.ToString();

    public static bool IsDevelopmentBuild =>
        Display.Equals("dev", StringComparison.OrdinalIgnoreCase)
        || AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug", StringComparison.OrdinalIgnoreCase);

    public static string RuntimeIdentifier =>
        OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    public static string GetUpdaterPath(string installDirectory) =>
        Path.Combine(installDirectory, OperatingSystem.IsWindows() ? "TS-DJ.Updater.exe" : "TS-DJ.Updater");

    public static string GetExpectedAssetName(Version version) =>
        $"TS-DJ-{version.Major}.{version.Minor}.{version.Build}-{(OperatingSystem.IsWindows() ? "win" : "linux")}-x64.zip";

    private static Version ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new Version(0, 0, 0);

        var semver = raw.Split('+', 2)[0];
        return Version.TryParse(semver, out var version) ? version : new Version(0, 0, 0);
    }
}
