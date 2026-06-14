namespace TS_DJ.Core.Models;

public sealed class TeamSpeakConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Nickname { get; set; } = "TS-DJ";
    public string ServerPassword { get; set; } = string.Empty;
    public string DefaultChannel { get; set; } = string.Empty;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name)
            ? (string.IsNullOrWhiteSpace(Address) ? "(unnamed)" : Address)
            : Name;
}
