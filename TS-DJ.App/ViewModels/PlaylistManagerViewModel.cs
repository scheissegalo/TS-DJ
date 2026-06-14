using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class PlaylistManagerViewModel : ViewModelBase
{
    private readonly ILogger<PlaylistManagerViewModel> _logger;
    private readonly IPlaylistService _playlistService;

    public ObservableCollection<SavedPlaylistSummary> Playlists { get; } = [];

    [ObservableProperty]
    private SavedPlaylistSummary? _selectedPlaylist;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    public bool CanRename => SelectedPlaylist is not null && !string.IsNullOrWhiteSpace(EditName);
    public bool CanDelete => SelectedPlaylist is not null;

    public PlaylistManagerViewModel(
        ILogger<PlaylistManagerViewModel> logger,
        IPlaylistService playlistService)
    {
        _logger = logger;
        _playlistService = playlistService;
    }

    public async Task LoadAsync()
    {
        try
        {
            var items = await _playlistService.ListAsync();
            Playlists.Clear();
            foreach (var item in items)
                Playlists.Add(item);

            SelectedPlaylist = Playlists.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playlists");
        }
    }

    partial void OnSelectedPlaylistChanged(SavedPlaylistSummary? value)
    {
        if (value is null)
        {
            EditName = string.Empty;
            EditDescription = string.Empty;
            return;
        }

        EditName = value.Name;
        EditDescription = value.Description ?? string.Empty;
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(CanDelete));
    }

    partial void OnEditNameChanged(string value) => OnPropertyChanged(nameof(CanRename));

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedPlaylist is null || !CanRename)
            return;

        try
        {
            await _playlistService.RenameAsync(SelectedPlaylist.Id, EditName);
            await _playlistService.UpdateMetadataAsync(SelectedPlaylist.Id, EditDescription);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename playlist");
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedPlaylist is null)
            return;

        try
        {
            var id = SelectedPlaylist.Id;
            await _playlistService.DeleteAsync(id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete playlist");
        }
    }
}
