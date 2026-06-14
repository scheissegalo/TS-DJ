using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class ConnectionProfileSelectorViewModel : ViewModelBase
{
    private readonly ILogger<ConnectionProfileSelectorViewModel> _logger;
    private readonly ISettingsService _settingsService;
    private TeamSpeakConnectionProfilesSettings _settings = new();

    public ObservableCollection<TeamSpeakConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    private TeamSpeakConnectionProfile? _selectedProfile;

    public event EventHandler? ProfilesChanged;

    public ConnectionProfileSelectorViewModel(
        ILogger<ConnectionProfileSelectorViewModel> logger,
        ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    public async Task LoadProfilesAsync()
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
            _logger.LogError(ex, "Failed to load connection profiles");
        }
    }

    partial void OnSelectedProfileChanged(TeamSpeakConnectionProfile? value)
    {
        if (value is null)
            return;

        _ = PersistSelectedProfileIdAsync();
    }

    public ConnectionSettings ToConnectionSettings(string channel)
    {
        var profile = SelectedProfile ?? throw new InvalidOperationException("No connection profile selected.");
        var effectiveChannel = string.IsNullOrWhiteSpace(channel) ? profile.DefaultChannel : channel;

        return new ConnectionSettings
        {
            Address = profile.Address,
            Nickname = profile.Nickname,
            ServerPassword = profile.ServerPassword,
            Channel = effectiveChannel
        };
    }

    public string GetDefaultChannel() =>
        SelectedProfile?.DefaultChannel ?? string.Empty;

    public async Task PersistSelectedProfileIdAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile is null)
            return;

        try
        {
            _settings.SelectedProfileId = SelectedProfile.Id;
            await _settingsService.SaveTeamSpeakConnectionProfilesAsync(_settings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save selected profile");
        }
    }

    public async Task UpdateProfileDefaultChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        if (SelectedProfile is null)
            return;

        try
        {
            SelectedProfile.DefaultChannel = channel;
            _settings.Profiles = Profiles.ToList();
            await _settingsService.SaveTeamSpeakConnectionProfilesAsync(_settings, cancellationToken);
            ProfilesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update profile default channel");
        }
    }

    public string SelectedNickname => SelectedProfile?.Nickname ?? "TS-DJ";
}
