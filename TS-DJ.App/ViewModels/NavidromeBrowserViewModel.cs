using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.App.ViewModels;

public partial class NavidromeBrowserViewModel : ViewModelBase
{
    private readonly INavidromeService _navidromeService;
    private readonly INavidromeMediaQueueService _mediaQueueService;
    private readonly ISettingsService _settingsService;
    private readonly ILogService _logService;

    [ObservableProperty]
    private string _serverUrl = "http://10.0.0.1:4533";

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = "Not connected";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private NavidromeTrackRowViewModel? _selectedTrack;

    [ObservableProperty]
    private NavidromeAlbumRowViewModel? _selectedAlbum;

    [ObservableProperty]
    private NavidromeArtistRowViewModel? _selectedArtist;

    [ObservableProperty]
    private NavidromePlaylistRowViewModel? _selectedPlaylist;

    [ObservableProperty]
    private NavidromeTrackRowViewModel? _selectedPlaylistTrack;

    public ObservableCollection<NavidromeTrackRowViewModel> Tracks { get; } = [];
    public ObservableCollection<NavidromeAlbumRowViewModel> Albums { get; } = [];
    public ObservableCollection<NavidromeArtistRowViewModel> Artists { get; } = [];
    public ObservableCollection<NavidromePlaylistRowViewModel> Playlists { get; } = [];
    public ObservableCollection<NavidromeTrackRowViewModel> PlaylistTracks { get; } = [];

    public NavidromeBrowserTab ActiveTab => (NavidromeBrowserTab)SelectedTabIndex;

    public bool CanConnect => !IsConnected && !IsBusy;
    public bool CanSearch => IsConnected && !IsBusy;
    public bool CanAddToQueue => IsConnected && !IsBusy && HasActiveTabSelection;

    public bool CanPlayNow => CanAddToQueue;

    public string AddToQueueLabel => ActiveTab switch
    {
        NavidromeBrowserTab.Tracks => "Add Track to Queue",
        NavidromeBrowserTab.Albums => "Add Album to Queue",
        NavidromeBrowserTab.Artists => "Add Artist to Queue",
        NavidromeBrowserTab.Playlists => "Add Playlist to Queue",
        _ => "Add to Queue"
    };

    public string PlayNowLabel => ActiveTab switch
    {
        NavidromeBrowserTab.Tracks => "Play Track Now",
        NavidromeBrowserTab.Albums => "Play Album Now",
        NavidromeBrowserTab.Artists => "Play Artist Now",
        NavidromeBrowserTab.Playlists => "Play Playlist Now",
        _ => "Play Now"
    };

    private bool HasActiveTabSelection => ActiveTab switch
    {
        NavidromeBrowserTab.Tracks => SelectedTrack is not null,
        NavidromeBrowserTab.Albums => SelectedAlbum is not null,
        NavidromeBrowserTab.Artists => SelectedArtist is not null,
        NavidromeBrowserTab.Playlists => SelectedPlaylist is not null,
        _ => false
    };

    public NavidromeBrowserViewModel(
        INavidromeService navidromeService,
        INavidromeMediaQueueService mediaQueueService,
        ISettingsService settingsService,
        ILogService logService)
    {
        _navidromeService = navidromeService;
        _mediaQueueService = mediaQueueService;
        _settingsService = settingsService;
        _logService = logService;

        _ = LoadSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsService.LoadNavidromeSettingsAsync();
            ServerUrl = settings.ServerUrl;
            Username = settings.Username;
            Password = settings.Password;

            if (!string.IsNullOrWhiteSpace(settings.Username))
                await ConnectInternalAsync(persistSettings: false);
        }
        catch (Exception ex)
        {
            LogError("Failed to load Navidrome settings", ex);
        }
    }

    [RelayCommand]
    private async Task ConnectAsync() => await ConnectInternalAsync(persistSettings: true);

    private async Task ConnectInternalAsync(bool persistSettings)
    {
        IsBusy = true;
        NotifyActionStateChanged();

        try
        {
            var settings = new NavidromeSettings
            {
                ServerUrl = ServerUrl,
                Username = Username,
                Password = Password
            };

            await _navidromeService.AuthenticateAsync(settings);
            IsConnected = true;
            ConnectionStatus = $"Connected to {NormalizeDisplayUrl(settings.ServerUrl)}";

            if (persistSettings)
                await _settingsService.SaveNavidromeSettingsAsync(settings);

            await LoadPlaylistsAsync();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ConnectionStatus = "Connection failed — see log";
            LogError("Navidrome connection failed", ex);
        }
        finally
        {
            IsBusy = false;
            NotifyActionStateChanged();
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(SearchQuery))
            return;

        IsBusy = true;
        NotifyActionStateChanged();

        try
        {
            var results = await _navidromeService.SearchAsync(SearchQuery.Trim());
            Tracks.Clear();
            Albums.Clear();
            Artists.Clear();
            SelectedTrack = null;
            SelectedAlbum = null;
            SelectedArtist = null;

            foreach (var track in results.Tracks)
                Tracks.Add(new NavidromeTrackRowViewModel(track));

            foreach (var album in results.Albums)
                Albums.Add(new NavidromeAlbumRowViewModel(album));

            foreach (var artist in results.Artists)
                Artists.Add(new NavidromeArtistRowViewModel(artist));

            SelectedTabIndex = Tracks.Count > 0 ? 0 : Albums.Count > 0 ? 1 : 0;
            ConnectionStatus = $"Found {Tracks.Count} tracks, {Albums.Count} albums, {Artists.Count} artists";
        }
        catch (Exception ex)
        {
            LogError("Navidrome search failed", ex);
            ConnectionStatus = "Search failed — see log";
        }
        finally
        {
            IsBusy = false;
            NotifyActionStateChanged();
        }
    }

    [RelayCommand]
    private async Task AddToQueueAsync() =>
        await ExecuteActiveTabActionAsync(playImmediately: false);

    [RelayCommand]
    private async Task PlayNowAsync() =>
        await ExecuteActiveTabActionAsync(playImmediately: true);

    public async Task PlayActiveTabSelectionNowAsync() =>
        await ExecuteActiveTabActionAsync(playImmediately: true);

    public async Task PlayTrackNowAsync(NavidromeTrack track) =>
        await EnqueueResolvedTracksAsync([track], playImmediately: true, contextLabel: track.DisplayName);

    private async Task ExecuteActiveTabActionAsync(bool playImmediately)
    {
        if (!CanAddToQueue)
            return;

        IsBusy = true;
        NotifyActionStateChanged();

        try
        {
            var tracks = await ResolveActiveTabTracksAsync();
            if (tracks.Count == 0)
            {
                ConnectionStatus = "No tracks found for selection";
                return;
            }

            var label = DescribeActiveSelection(tracks.Count);
            await EnqueueResolvedTracksAsync(tracks, playImmediately, label);
        }
        catch (Exception ex)
        {
            LogError("Failed to queue selection", ex);
            ConnectionStatus = "Queue action failed — see log";
        }
        finally
        {
            IsBusy = false;
            NotifyActionStateChanged();
        }
    }

    private async Task<IReadOnlyList<NavidromeTrack>> ResolveActiveTabTracksAsync() =>
        ActiveTab switch
        {
            NavidromeBrowserTab.Tracks when SelectedTrack is not null =>
                [SelectedTrack.Track],
            NavidromeBrowserTab.Albums when SelectedAlbum is not null =>
                await _navidromeService.ResolveTracksAsync(NavidromeMediaKind.Album, SelectedAlbum.Album.Id),
            NavidromeBrowserTab.Artists when SelectedArtist is not null =>
                await _navidromeService.ResolveTracksAsync(NavidromeMediaKind.Artist, SelectedArtist.Artist.Id),
            NavidromeBrowserTab.Playlists when SelectedPlaylist is not null =>
                await _navidromeService.ResolveTracksAsync(NavidromeMediaKind.Playlist, SelectedPlaylist.Playlist.Id),
            _ => []
        };

    private string DescribeActiveSelection(int trackCount) =>
        ActiveTab switch
        {
            NavidromeBrowserTab.Tracks => SelectedTrack?.Track.DisplayName ?? "track",
            NavidromeBrowserTab.Albums => SelectedAlbum is null
                ? "album"
                : $"{SelectedAlbum.Name} ({trackCount} tracks)",
            NavidromeBrowserTab.Artists => SelectedArtist is null
                ? "artist"
                : $"{SelectedArtist.Name} ({trackCount} tracks)",
            NavidromeBrowserTab.Playlists => SelectedPlaylist is null
                ? "playlist"
                : $"{SelectedPlaylist.Name} ({trackCount} tracks)",
            _ => "selection"
        };

    private async Task EnqueueResolvedTracksAsync(
        IReadOnlyList<NavidromeTrack> tracks,
        bool playImmediately,
        string contextLabel)
    {
        await _mediaQueueService.EnqueueTracksAsync(tracks, playImmediately);
        ConnectionStatus = playImmediately
            ? $"Playing: {contextLabel}"
            : $"Queued: {contextLabel}";
    }

    private async Task LoadPlaylistsAsync()
    {
        try
        {
            var playlists = await _navidromeService.GetPlaylistsAsync();
            Playlists.Clear();
            SelectedPlaylist = null;
            PlaylistTracks.Clear();
            SelectedPlaylistTrack = null;

            foreach (var playlist in playlists)
                Playlists.Add(new NavidromePlaylistRowViewModel(playlist));
        }
        catch (Exception ex)
        {
            LogError("Failed to load Navidrome playlists", ex);
        }
    }

    partial void OnSelectedPlaylistChanged(NavidromePlaylistRowViewModel? value)
    {
        _ = LoadSelectedPlaylistTracksAsync();
        NotifyActionStateChanged();
    }

    private async Task LoadSelectedPlaylistTracksAsync()
    {
        PlaylistTracks.Clear();
        SelectedPlaylistTrack = null;

        if (SelectedPlaylist is null || !IsConnected)
        {
            NotifyActionStateChanged();
            return;
        }

        IsBusy = true;
        NotifyActionStateChanged();

        try
        {
            var tracks = await _navidromeService.GetPlaylistTracksAsync(SelectedPlaylist.Playlist.Id);
            foreach (var track in tracks)
                PlaylistTracks.Add(new NavidromeTrackRowViewModel(track));
        }
        catch (Exception ex)
        {
            LogError("Failed to load playlist tracks", ex);
        }
        finally
        {
            IsBusy = false;
            NotifyActionStateChanged();
        }
    }

    partial void OnSelectedTabIndexChanged(int value) => NotifyActionStateChanged();
    partial void OnSelectedTrackChanged(NavidromeTrackRowViewModel? value) => NotifyActionStateChanged();
    partial void OnSelectedAlbumChanged(NavidromeAlbumRowViewModel? value) => NotifyActionStateChanged();
    partial void OnSelectedArtistChanged(NavidromeArtistRowViewModel? value) => NotifyActionStateChanged();
    partial void OnSelectedPlaylistTrackChanged(NavidromeTrackRowViewModel? value) { }
    partial void OnIsConnectedChanged(bool value) => NotifyActionStateChanged();
    partial void OnIsBusyChanged(bool value) => NotifyActionStateChanged();

    private void NotifyActionStateChanged()
    {
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(CanAddToQueue));
        OnPropertyChanged(nameof(CanPlayNow));
        OnPropertyChanged(nameof(AddToQueueLabel));
        OnPropertyChanged(nameof(PlayNowLabel));
    }

    private static string NormalizeDisplayUrl(string url) =>
        url.Trim().TrimEnd('/');

    private void LogError(string message, Exception ex)
    {
        var detail = ex.InnerException is null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})";
        Dispatcher.UIThread.Post(() =>
        {
            _logService.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = "Error",
                Message = $"{message}: {detail}"
            });
        });
    }
}
