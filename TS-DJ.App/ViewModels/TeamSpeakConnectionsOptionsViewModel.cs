using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class TeamSpeakConnectionsOptionsViewModel : ViewModelBase
{
    private readonly ILogger<TeamSpeakConnectionsOptionsViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private TeamSpeakConnectionProfilesSettings _settings = new();

    public ObservableCollection<TeamSpeakConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    private TeamSpeakConnectionProfile? _selectedProfile;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editAddress = string.Empty;

    [ObservableProperty]
    private string _editNickname = "TS-DJ";

    [ObservableProperty]
    private string _editServerPassword = string.Empty;

    [ObservableProperty]
    private string _editDefaultChannel = string.Empty;

    public bool CanSaveProfile => !string.IsNullOrWhiteSpace(EditAddress);

    public TeamSpeakConnectionsOptionsViewModel(
        ILogger<TeamSpeakConnectionsOptionsViewModel> logger,
        ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task LoadAsync()
    {
        try
        {
            _settings = await _settingsService.LoadTeamSpeakConnectionProfilesAsync();
            Profiles.Clear();
            foreach (var profile in _settings.Profiles)
                Profiles.Add(profile);

            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _settings.SelectedProfileId)
                ?? Profiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TeamSpeak connection profiles");
        }
    }

    partial void OnSelectedProfileChanged(TeamSpeakConnectionProfile? value)
    {
        if (value is null)
        {
            ClearEditor();
            return;
        }

        EditName = value.Name;
        EditAddress = value.Address;
        EditNickname = value.Nickname;
        EditServerPassword = value.ServerPassword;
        EditDefaultChannel = value.DefaultChannel;
    }

    partial void OnEditAddressChanged(string value) => OnPropertyChanged(nameof(CanSaveProfile));

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new TeamSpeakConnectionProfile
        {
            Name = "New Server",
            Nickname = "TS-DJ"
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (SelectedProfile is null || !CanSaveProfile)
            return;

        ApplyEditorToProfile(SelectedProfile);
        await PersistAsync();
    }

    [RelayCommand]
    private async Task RemoveProfileAsync()
    {
        if (SelectedProfile is null || Profiles.Count <= 1)
            return;

        var removed = SelectedProfile;
        Profiles.Remove(removed);
        SelectedProfile = Profiles.FirstOrDefault();
        await PersistAsync();
    }

    private void ApplyEditorToProfile(TeamSpeakConnectionProfile profile)
    {
        profile.Name = EditName.Trim();
        profile.Address = EditAddress.Trim();
        profile.Nickname = string.IsNullOrWhiteSpace(EditNickname) ? "TS-DJ" : EditNickname.Trim();
        profile.ServerPassword = EditServerPassword;
        profile.DefaultChannel = EditDefaultChannel.Trim();
    }

    private async Task PersistAsync()
    {
        _settings.Profiles = Profiles.ToList();
        _settings.SelectedProfileId = SelectedProfile?.Id;
        await _settingsService.SaveTeamSpeakConnectionProfilesAsync(_settings);
    }

    private void ClearEditor()
    {
        EditName = string.Empty;
        EditAddress = string.Empty;
        EditNickname = "TS-DJ";
        EditServerPassword = string.Empty;
        EditDefaultChannel = string.Empty;
    }
}
