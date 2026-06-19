using System.Diagnostics;

namespace TS_DJ.Infrastructure.YtDlp;

public sealed class YtDlpProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
}

public sealed class YtDlpProcessRunner
{
    public async Task<string> RunCaptureStdoutAsync(
        string executable,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var bytes = await RunCaptureStdoutBytesAsync(executable, args, timeout, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public async Task RunAsync(
        string executable,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var process = CreateProcess(executable, args, captureStdout: false);

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
            throw new YtDlpException("yt-dlp timed out.");
        }
        catch (YtDlpException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            throw new YtDlpException("yt-dlp process failed.", ex);
        }
    }

    public async Task<byte[]> RunCaptureStdoutBytesAsync(
        string executable,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var process = CreateProcess(executable, args, captureStdout: true);

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
            throw new YtDlpException("yt-dlp timed out.");
        }
        catch (YtDlpException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            throw new YtDlpException("yt-dlp process failed.", ex);
        }
    }

    public async Task<bool> ValidateExecutableAsync(
        string executable,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executable))
            return false;

        try
        {
            await RunCaptureStdoutAsync(executable, ["--version"], timeout, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string FormatCommandLine(string executable, IReadOnlyList<string> args) =>
        $"{executable} {string.Join(' ', args.Select(QuoteForDisplay))}";

    public static YtDlpException MapProcessFailure(string stderr, int exitCode)
    {
        var text = stderr.Trim();
        var lower = text.ToLowerInvariant();

        if (lower.Contains("http error 403") || lower.Contains("403 forbidden") || lower.Contains(" 403 "))
            return new YtDlpException(
                "YouTube denied the media download (HTTP 403). Update yt-dlp, configure a JS runtime in Options, or retry playback.");

        if (lower.Contains("private video") || lower.Contains("sign in") || lower.Contains("login"))
            return new YtDlpException("Video is private or requires sign-in.");

        if (lower.Contains("age") && lower.Contains("restrict"))
            return new YtDlpException("Video is age-restricted.");

        if (lower.Contains("unviewable") || lower.Contains("playlist type is unviewable"))
            return new YtDlpException(
                "This YouTube Mix or radio playlist cannot be opened from a playlist link alone. "
                + "Paste the URL from the video page (watch URL with list= parameter).");

        if (lower.Contains("unavailable") || lower.Contains("removed") || lower.Contains("not available"))
            return new YtDlpException("Video is unavailable.");

        if (lower.Contains("js runtime") || lower.Contains("js challenge") || lower.Contains("ejs"))
            return new YtDlpException(
                "YouTube requires a JavaScript runtime for this video. Configure one in Options → YouTube / yt-dlp.");

        if (string.IsNullOrWhiteSpace(text))
            return new YtDlpException($"yt-dlp failed with exit code {exitCode}.");

        var firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        return new YtDlpException(firstLine.Length > 200 ? firstLine[..200] : firstLine);
    }

    private static Process CreateProcess(string executable, IReadOnlyList<string> args, bool captureStdout)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (captureStdout)
            process.StartInfo.RedirectStandardOutput = true;

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        return process;
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    private static string QuoteForDisplay(string arg) =>
        arg.Contains(' ') || arg.Contains('"') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;

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
