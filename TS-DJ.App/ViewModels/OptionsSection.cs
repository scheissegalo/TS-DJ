namespace TS_DJ.App.ViewModels;

public enum OptionsSection
{
    TeamSpeakConnections,
    Audio,
    Navidrome,
    Soundboard,
    UiPreferences
}

public sealed class OptionsSectionItem
{
    public OptionsSectionItem(OptionsSection section, string title)
    {
        Section = section;
        Title = title;
    }

    public OptionsSection Section { get; }
    public string Title { get; }
}
