namespace TS_DJ.Core.Models;

public enum TeamSpeakMessageTarget
{
    Private,
    Channel,
    Server
}

public sealed class TeamSpeakTextMessage
{
    public required string InvokerClientId { get; init; }
    public required string InvokerName { get; init; }
    public required string Message { get; init; }
    public TeamSpeakMessageTarget Target { get; init; }
}
