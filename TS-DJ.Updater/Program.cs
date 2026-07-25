using System.Diagnostics;
using System.IO.Compression;

namespace TS_DJ.Updater;

internal static class Program
{
    private static readonly TimeSpan ProcessWaitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessPollInterval = TimeSpan.FromMilliseconds(200);

    public static int Main(string[] args)
    {
        try
        {
            var options = UpdaterOptions.Parse(args);
            return Run(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TS-DJ updater failed: {ex.Message}");
            return 1;
        }
    }

    private static int Run(UpdaterOptions options)
    {
        Console.Error.WriteLine($"Waiting for process {options.WaitPid} to exit...");
        if (!WaitForProcessExit(options.WaitPid, ProcessWaitTimeout))
            Console.Error.WriteLine("Warning: timed out waiting for parent process; continuing anyway.");

        var extractRoot = Path.Combine(Path.GetTempPath(), "TS-DJ-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            Console.Error.WriteLine($"Extracting {options.PackagePath}...");
            ZipFile.ExtractToDirectory(options.PackagePath, extractRoot);

            var sourceDir = FindPackageRoot(extractRoot);
            Console.Error.WriteLine($"Installing from {sourceDir} into {options.InstallDir}...");
            CopyDirectory(sourceDir, options.InstallDir);

            if (OperatingSystem.IsLinux())
                MakeExecutable(Path.Combine(options.InstallDir, "TS-DJ.App"));

            var wrapperPath = Path.Combine(options.InstallDir, "ts-dj");
            if (File.Exists(wrapperPath))
                MakeExecutable(wrapperPath);

            Console.Error.WriteLine($"Restarting {options.RestartPath}...");
            StartDetached(options.RestartPath);
            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractRoot))
                    Directory.Delete(extractRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private static bool WaitForProcessExit(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (!IsProcessRunning(pid))
                return true;

            Thread.Sleep(ProcessPollInterval);
        }

        return !IsProcessRunning(pid);
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            process.Dispose();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string FindPackageRoot(string extractRoot)
    {
        var topLevelEntries = Directory.GetFileSystemEntries(extractRoot);
        if (topLevelEntries.Length == 1 && Directory.Exists(topLevelEntries[0]))
            return topLevelEntries[0];

        return extractRoot;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(destinationDir, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void MakeExecutable(string path)
    {
        if (!File.Exists(path))
            return;

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
    }

    private static void StartDetached(string executablePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    private sealed class UpdaterOptions
    {
        public required int WaitPid { get; init; }
        public required string InstallDir { get; init; }
        public required string PackagePath { get; init; }
        public required string RestartPath { get; init; }

        public static UpdaterOptions Parse(string[] args)
        {
            int? waitPid = null;
            string? installDir = null;
            string? packagePath = null;
            string? restartPath = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--wait-pid" when i + 1 < args.Length && int.TryParse(args[++i], out var pid):
                        waitPid = pid;
                        break;
                    case "--install-dir" when i + 1 < args.Length:
                        installDir = args[++i];
                        break;
                    case "--package" when i + 1 < args.Length:
                        packagePath = args[++i];
                        break;
                    case "--restart" when i + 1 < args.Length:
                        restartPath = args[++i];
                        break;
                }
            }

            if (waitPid is null || string.IsNullOrWhiteSpace(installDir))
                throw new ArgumentException("Missing required arguments. Usage: --wait-pid <pid> --install-dir <dir> --package <zip> --restart <exe>");

            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
                throw new FileNotFoundException("Package zip not found.", packagePath);

            if (string.IsNullOrWhiteSpace(restartPath) || !File.Exists(restartPath))
                throw new FileNotFoundException("Restart executable not found.", restartPath);

            return new UpdaterOptions
            {
                WaitPid = waitPid.Value,
                InstallDir = Path.GetFullPath(installDir),
                PackagePath = Path.GetFullPath(packagePath),
                RestartPath = Path.GetFullPath(restartPath)
            };
        }
    }
}
