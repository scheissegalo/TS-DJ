namespace TS_DJ.Core.Models;

public sealed class ConnectionSettings
{
    public string Address { get; set; } = string.Empty;
    public string Nickname { get; set; } = "TS-DJ";
    public string ServerPassword { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string IdentityPrivateKey { get; set; } = string.Empty;
    public int IdentityOffset { get; set; }
    public int SecurityLevel { get; set; } = 8;
}
