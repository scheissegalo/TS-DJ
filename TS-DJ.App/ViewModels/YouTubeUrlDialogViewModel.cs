using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using TS_DJ.App.Services;
using TS_DJ.Infrastructure.Media;
using TS_DJ.Infrastructure.YtDlp;

namespace TS_DJ.App.ViewModels;

public partial class YouTubeUrlDialogViewModel : ViewModelBase
{
    private readonly ILogger<YouTubeUrlDialogViewModel> _logger;
    private readonly YoutubeMediaSource _youtubeMediaSource;
    private readonly IYoutubeMediaQueueService _youtubeMediaQueueService;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private bool _isPlaylistMode;

    [ObservableProperty]
    private string _previewTitle = string.Empty;

    [ObservableProperty]
    private string _previewUploader = string.Empty;

    [ObservableProperty]
    private string _previewDuration = string.Empty;

    [ObservableProperty]
    private string _previewVideoCount = string.Empty;

    [ObservableProperty]
    private string? _previewThumbnailUrl;

    public bool CanConfirm => !IsBusy && HasPreview && !HasError;

    public bool IsSingleVideoMode => !IsPlaylistMode;

    public string AddButtonText => IsPlaylistMode ? "Add Playlist To Queue" : "Add to Queue";

    public string PlayButtonText => IsPlaylistMode ? "Play Playlist Now" : "Play Now";

    public YouTubeUrlDialogViewModel(
        ILogger<YouTubeUrlDialogViewModel> logger,
        YoutubeMediaSource youtubeMediaSource,
        IYoutubeMediaQueueService youtubeMediaQueueService)
    {
        _logger = logger;
        _youtubeMediaSource = youtubeMediaSource;
        _youtubeMediaQueueService = youtubeMediaQueueService;
    }

    partial void OnUrlChanged(string value)
    {
        HasPreview = false;
        IsPlaylistMode = false;
        HasError = false;
        StatusMessage = string.Empty;
        _ = ResolveMetadataAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(AddButtonText));
        OnPropertyChanged(nameof(PlayButtonText));
    }

    partial void OnHasPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(AddButtonText));
        OnPropertyChanged(nameof(PlayButtonText));
    }

    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(CanConfirm));

    partial void OnIsPlaylistModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSingleVideoMode));
        OnPropertyChanged(nameof(AddButtonText));
        OnPropertyChanged(nameof(PlayButtonText));
    }

    private async Task ResolveMetadataAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
            return;

        if (!YoutubeUrlHelper.TryClassify(Url, out var classification))
        {
            HasError = true;
            HasPreview = false;
            StatusMessage = "Enter a valid YouTube video or playlist URL.";
            return;
        }

        IsBusy = true;
        HasError = false;

        try
        {
            if (classification.Kind == YoutubeContentKind.Playlist)
            {
                await ResolvePlaylistPreviewAsync();
                return;
            }

            await ResolveSingleVideoPreviewAsync();
        }
        catch (YtDlpException ex)
        {
            HasError = true;
            HasPreview = false;
            StatusMessage = ex.Message;
            _logger.LogWarning(ex, "YouTube metadata resolution failed");
        }
        catch (Exception ex)
        {
            HasError = true;
            HasPreview = false;
            StatusMessage = "Failed to resolve YouTube URL.";
            _logger.LogError(ex, "YouTube metadata resolution failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResolveSingleVideoPreviewAsync()
    {
        StatusMessage = "Resolving video metadata…";
        IsPlaylistMode = false;

        var item = await _youtubeMediaSource.TryCreateFromInputAsync(Url);
        if (item is null)
        {
            HasError = true;
            HasPreview = false;
            StatusMessage = "Invalid YouTube video URL.";
            return;
        }

        PreviewTitle = item.DisplayName;
        PreviewUploader = item.Artist ?? "Unknown uploader";
        PreviewDuration = item.DurationSeconds is > 0
            ? FormatDuration(item.DurationSeconds.Value)
            : "Unknown duration";
        PreviewVideoCount = string.Empty;
        PreviewThumbnailUrl = item.ThumbnailUrl;
        HasPreview = true;
        StatusMessage = "Ready to add or play.";
    }

    private async Task ResolvePlaylistPreviewAsync()
    {
        StatusMessage = "Resolving playlist metadata…";
        IsPlaylistMode = true;

        var playlist = await _youtubeMediaQueueService.FetchPlaylistPreviewAsync(Url);
        PreviewTitle = playlist.Title;
        PreviewUploader = "YouTube Playlist";
        PreviewDuration = string.Empty;
        PreviewVideoCount = $"{playlist.VideoCount} videos";
        PreviewThumbnailUrl = null;
        HasPreview = true;
        StatusMessage = "Ready to import playlist.";
    }

    public async Task<bool> AddToQueueAsync()
    {
        if (!CanConfirm)
            return false;

        return await EnqueueAsync(playImmediately: false);
    }

    public async Task<bool> PlayNowAsync()
    {
        if (!CanConfirm)
            return false;

        return await EnqueueAsync(playImmediately: true);
    }

    private async Task<bool> EnqueueAsync(bool playImmediately)
    {
        IsBusy = true;
        StatusMessage = playImmediately
            ? IsPlaylistMode ? "Starting playlist…" : "Starting playback…"
            : IsPlaylistMode ? "Importing playlist…" : "Adding to queue…";

        try
        {
            if (IsPlaylistMode)
            {
                await _youtubeMediaQueueService.EnqueuePlaylistAsync(
                    Url,
                    playImmediately,
                    replaceQueue: playImmediately);
            }
            else
            {
                await _youtubeMediaQueueService.EnqueueUrlAsync(Url, playImmediately);
            }

            return true;
        }
        catch (YtDlpException ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
            _logger.LogWarning(ex, "YouTube enqueue failed");
            return false;
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = IsPlaylistMode
                ? "Failed to import YouTube playlist."
                : "Failed to enqueue YouTube video.";
            _logger.LogError(ex, "YouTube enqueue failed");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatDuration(int totalSeconds)
    {
        var time = TimeSpan.FromSeconds(totalSeconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }
}
