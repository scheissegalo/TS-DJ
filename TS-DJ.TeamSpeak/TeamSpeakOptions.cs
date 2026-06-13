namespace TS_DJ.TeamSpeak;

public sealed class TeamSpeakOptions
{
    public int SecurityLevel { get; set; } = 8;

    public TimeSpan[] TimeoutReconnectDelays { get; set; } =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5)
    ];

    public TimeSpan[] ErrorReconnectDelays { get; set; } =
    [
        TimeSpan.FromSeconds(30)
    ];

    public TimeSpan[] ServerShutdownReconnectDelays { get; set; } =
    [
        TimeSpan.FromMinutes(5)
    ];
}
