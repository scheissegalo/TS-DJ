namespace TS_DJ.Core.Models;

public sealed class NavidromeSettings
{
    public const string ConfigKey = "navidrome.config";

    public string ServerUrl { get; set; } = "http://10.0.0.1:4533";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
