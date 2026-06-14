namespace TS_DJ.Core.Models;

public sealed class TeamSpeakConnectionProfilesSettings
{
    public const string ConfigKey = "connection.profiles";

    public List<TeamSpeakConnectionProfile> Profiles { get; set; } = [];
    public Guid? SelectedProfileId { get; set; }
}
