using CommunityToolkit.Mvvm.ComponentModel;
using TS_DJ.Core.Models;

namespace TS_DJ.App.ViewModels;

public sealed partial class NavidromeTrackRowViewModel : ObservableObject
{
    public NavidromeTrackRowViewModel(NavidromeTrack track)
    {
        Track = track;
    }

    public NavidromeTrack Track { get; }

    public string Title => Track.Title;
    public string Artist => Track.Artist;
    public string Album => Track.Album;
    public string DurationDisplay => MainWindowViewModel.FormatPlaybackTime(Track.Duration);
}

public sealed partial class NavidromeAlbumRowViewModel : ObservableObject
{
    public NavidromeAlbumRowViewModel(NavidromeAlbum album)
    {
        Album = album;
    }

    public NavidromeAlbum Album { get; }

    public string Name => Album.Name;
    public string Artist => Album.Artist;
    public int SongCount => Album.SongCount;
    public string DurationDisplay => MainWindowViewModel.FormatPlaybackTime(TimeSpan.FromSeconds(Album.DurationSeconds));
}

public sealed partial class NavidromeArtistRowViewModel : ObservableObject
{
    public NavidromeArtistRowViewModel(NavidromeArtist artist)
    {
        Artist = artist;
    }

    public NavidromeArtist Artist { get; }

    public string Name => Artist.Name;
    public int AlbumCount => Artist.AlbumCount;
}

public sealed partial class NavidromePlaylistRowViewModel : ObservableObject
{
    public NavidromePlaylistRowViewModel(NavidromePlaylist playlist)
    {
        Playlist = playlist;
    }

    public NavidromePlaylist Playlist { get; }

    public string Name => Playlist.Name;
    public int SongCount => Playlist.SongCount;
    public string DurationDisplay => MainWindowViewModel.FormatPlaybackTime(TimeSpan.FromSeconds(Playlist.DurationSeconds));
}
