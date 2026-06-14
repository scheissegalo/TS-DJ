using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TS_DJ.App.Views;

namespace TS_DJ.App.ViewModels;

public partial class OptionsViewModel : ViewModelBase
{
    public ObservableCollection<OptionsSectionItem> Sections { get; } =
    [
        new(OptionsSection.TeamSpeakConnections, "TeamSpeak"),
        new(OptionsSection.Audio, "Audio"),
        new(OptionsSection.Navidrome, "Navidrome"),
        new(OptionsSection.Soundboard, "Soundboard"),
        new(OptionsSection.UiPreferences, "UI / Preferences")
    ];

    public TeamSpeakConnectionsOptionsViewModel TeamSpeak { get; }
    public AudioOptionsViewModel Audio { get; }
    public NavidromeOptionsViewModel Navidrome { get; }
    public SoundboardOptionsViewModel Soundboard { get; }
    public UiPreferencesOptionsViewModel UiPreferences { get; }

    [ObservableProperty]
    private OptionsSectionItem? _selectedSection;

    public object? CurrentSectionContent => SelectedSection?.Section switch
    {
        OptionsSection.TeamSpeakConnections => TeamSpeak,
        OptionsSection.Audio => Audio,
        OptionsSection.Navidrome => Navidrome,
        OptionsSection.Soundboard => Soundboard,
        OptionsSection.UiPreferences => UiPreferences,
        _ => null
    };

    public OptionsViewModel(
        TeamSpeakConnectionsOptionsViewModel teamSpeak,
        AudioOptionsViewModel audio,
        NavidromeOptionsViewModel navidrome,
        SoundboardOptionsViewModel soundboard,
        UiPreferencesOptionsViewModel uiPreferences)
    {
        TeamSpeak = teamSpeak;
        Audio = audio;
        Navidrome = navidrome;
        Soundboard = soundboard;
        UiPreferences = uiPreferences;
        SelectedSection = Sections[0];
    }

    public async Task LoadAllAsync()
    {
        await TeamSpeak.LoadAsync();
        await Audio.LoadAsync();
        await Navidrome.LoadAsync();
        await Soundboard.LoadAsync();
        await UiPreferences.LoadAsync();
    }

    partial void OnSelectedSectionChanged(OptionsSectionItem? value) =>
        OnPropertyChanged(nameof(CurrentSectionContent));

    [RelayCommand]
    private void OpenWithSection(OptionsSection section)
    {
        SelectedSection = Sections.FirstOrDefault(s => s.Section == section) ?? Sections[0];
    }

    public static void Show(OptionsSection section = OptionsSection.TeamSpeakConnections)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var viewModel = App.Services.GetRequiredService<OptionsViewModel>();
        viewModel.OpenWithSectionCommand.Execute(section);

        var window = new OptionsWindow
        {
            DataContext = viewModel
        };

        if (desktop.MainWindow is Avalonia.Controls.Window owner)
            window.Show(owner);
        else
            window.Show();
    }
}
