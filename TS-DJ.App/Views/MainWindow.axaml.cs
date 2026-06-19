using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TS_DJ.App.Services;
using TS_DJ.App.ViewModels;
using TS_DJ.Core.Models;

namespace TS_DJ.App.Views;

public partial class MainWindow : Window
{
    private readonly ApplicationShutdownService _shutdownService;
    private readonly ILogger<MainWindow> _logger;
    private bool _isShuttingDown;
    private MainWindowViewModel? _viewModel;
    private bool _logAutoScrollEnabled = true;
    private bool _isProgrammaticScroll;
    private bool _logScrollWatcherAttached;
    private int _logScrollGeneration;
    private ScrollViewer? _logScrollViewer;

    public MainWindow()
    {
        InitializeComponent();
        _shutdownService = App.Services.GetRequiredService<ApplicationShutdownService>();
        _logger = App.Services.GetRequiredService<ILogger<MainWindow>>();
        DataContextChanged += OnDataContextChanged;
        LogListBox.LayoutUpdated += OnLogListBoxLayoutUpdated;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.LogEntryAppended -= OnLogEntryAppended;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.LogEntryAppended += OnLogEntryAppended;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.CurrentlyPlayingItem))
            return;

        if (sender is not MainWindowViewModel vm)
            return;

        ScrollQueueToPlayingItem(vm.CurrentlyPlayingItem);
    }

    private void ScrollQueueToPlayingItem(PlaybackQueueItemViewModel? item)
    {
        if (_viewModel is null)
            return;

        if (item is null)
        {
            _viewModel.TransitionProfiler.MarkUiComplete(TimeSpan.Zero);
            return;
        }

        var scrollSw = Stopwatch.StartNew();
        Dispatcher.UIThread.Post(() =>
        {
            QueueListBox.ScrollIntoView(item);
            scrollSw.Stop();
            _viewModel.TransitionProfiler.MarkUiComplete(scrollSw.Elapsed);
            _logger.LogDebug("Queue auto-scrolled to {SourceKey}", item.SourceKey);
        }, DispatcherPriority.Background);
    }

    private void OnLogEntryAppended(object? sender, LogEntry entry) => RequestLogScroll();

    private void RequestLogScroll()
    {
        if (!_logAutoScrollEnabled)
            return;

        var generation = ++_logScrollGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _logScrollGeneration || !_logAutoScrollEnabled)
                return;

            ScrollLogToBottom();
        }, DispatcherPriority.Loaded);
    }

    private void ScrollLogToBottom()
    {
        if (_viewModel?.LogEntries is not { Count: > 0 })
            return;

        var scrollViewer = GetLogScrollViewer();
        var last = _viewModel.LogEntries[^1];

        _isProgrammaticScroll = true;
        try
        {
            LogListBox.ScrollIntoView(last);

            if (scrollViewer is not null)
            {
                var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                scrollViewer.Offset = new Vector(0, maxOffset);
            }
        }
        finally
        {
            _isProgrammaticScroll = false;
        }

        _logger.LogTrace("Log auto-scrolled to latest entry");
    }

    private ScrollViewer? GetLogScrollViewer()
    {
        if (_logScrollViewer is not null)
            return _logScrollViewer;

        _logScrollViewer = LogListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        return _logScrollViewer;
    }

    private void OnLogListBoxLayoutUpdated(object? sender, EventArgs e)
    {
        if (_logScrollWatcherAttached)
            return;

        var scrollViewer = GetLogScrollViewer();
        if (scrollViewer is null)
            return;

        scrollViewer.ScrollChanged += OnLogScrollChanged;
        _logScrollWatcherAttached = true;
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScroll || sender is not ScrollViewer scrollViewer)
            return;

        var atBottom = scrollViewer.Offset.Y >= scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 4;
        if (_logAutoScrollEnabled == atBottom)
            return;

        _logAutoScrollEnabled = atBottom;
        _logger.LogDebug("Log auto-scroll {State}", atBottom ? "resumed" : "paused");

        if (atBottom)
            RequestLogScroll();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await RestoreWindowSettingsAsync();
        RequestLogScroll();
        Focus();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isShuttingDown)
            return;

        e.Cancel = true;
        _isShuttingDown = true;

        await SaveWindowSettingsAsync();
        await _shutdownService.ShutdownAsync();
        Close();
    }

    private async Task RestoreWindowSettingsAsync()
    {
        var settings = await _shutdownService.LoadWindowSettingsAsync();

        if (settings.Width is > 720)
            Width = settings.Width.Value;
        if (settings.Height is > 520)
            Height = settings.Height.Value;

        if (settings.X is not null && settings.Y is not null)
            Position = new PixelPoint((int)settings.X.Value, (int)settings.Y.Value);

        WindowState = settings.WindowState switch
        {
            "Maximized" => WindowState.Maximized,
            "Minimized" => WindowState.Minimized,
            _ => WindowState.Normal
        };
    }

    private async Task SaveWindowSettingsAsync()
    {
        var state = WindowState switch
        {
            WindowState.Maximized => "Maximized",
            WindowState.Minimized => "Minimized",
            _ => "Normal"
        };

        await _shutdownService.SaveWindowSettingsAsync(
            Width,
            Height,
            Position.X,
            Position.Y,
            state,
            DataContext is MainWindowViewModel vm && vm.IsSoundboardVisible);
    }

    private void QueueListBox_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.PlaySelectedQueueItemCommand.CanExecute(null))
            viewModel.PlaySelectedQueueItemCommand.Execute(null);
    }

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel mainVm)
            return;

        if (mainVm.Soundboard.TryHandleHotkey(e.Key, e.KeyModifiers))
            e.Handled = true;
    }
}
