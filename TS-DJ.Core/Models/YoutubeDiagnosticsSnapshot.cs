namespace TS_DJ.Core.Models;

public enum YoutubeDiagnosticsStatus
{
    Ready,
    Degraded,
    Error,
    NotFound
}

public sealed class YoutubeDiagnosticsSnapshot
{
    public YoutubeDiagnosticsStatus Status { get; init; } = YoutubeDiagnosticsStatus.NotFound;
    public string? YtDlpPath { get; init; }
    public string? YtDlpOrigin { get; init; }
    public string? YtDlpVersion { get; init; }
    public string JsRuntimeStatus { get; init; } = "Not detected";
    public string? JsRuntimeName { get; init; }
    public string? JsRuntimePath { get; init; }
    public string? StatusMessage { get; init; }

    public string StatusDisplay => Status switch
    {
        YoutubeDiagnosticsStatus.Ready => "Ready",
        YoutubeDiagnosticsStatus.Degraded => "Degraded",
        YoutubeDiagnosticsStatus.Error => "Error",
        YoutubeDiagnosticsStatus.NotFound => "Not found",
        _ => Status.ToString()
    };
}
