using Microsoft.Extensions.Logging;
using TS_DJ.Audio.Mixing;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;
using TS_DJ.TeamSpeak;

namespace TS_DJ.Audio;

/// <summary>
/// Facade for soundboard pad configuration and playback triggers.
/// </summary>
public sealed class SoundboardService : ISoundboardService
{
    private readonly ILogger<SoundboardService> _logger;
    private readonly IAudioMixerService _mixer;
    private readonly AudioMixerService _mixerImpl;
    private readonly TeamSpeakService _teamSpeak;
    private readonly ISettingsService _settingsService;
    private readonly object _sync = new();
    private SoundboardSettings _settings = new();

    public SoundboardService(
        ILogger<SoundboardService> logger,
        IAudioMixerService mixer,
        AudioMixerService mixerImpl,
        TeamSpeakService teamSpeak,
        ISettingsService settingsService)
    {
        _logger = logger;
        _mixer = mixer;
        _mixerImpl = mixerImpl;
        _teamSpeak = teamSpeak;
        _settingsService = settingsService;
    }

    public IReadOnlyList<SoundboardPad> Pads
    {
        get
        {
            lock (_sync)
                return _settings.Pads.ToList();
        }
    }

    public float Volume
    {
        get => _mixer.SoundboardVolume;
        set
        {
            _mixer.SoundboardVolume = Math.Clamp(value, 0f, 1f);
            lock (_sync)
                _settings.SoundboardVolumeHuman = (int)Math.Round(AudioValues.FactorToHumanVolume(_mixer.SoundboardVolume));
        }
    }

    public event EventHandler? PadsChanged;
    public event EventHandler<int>? PadTriggered;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _settingsService.LoadSoundboardSettingsAsync(cancellationToken);
        lock (_sync)
            _settings = loaded;

        _mixer.SoundboardVolume = AudioValues.HumanVolumeToFactor(_settings.SoundboardVolumeHuman);

        foreach (var pad in _settings.Pads)
        {
            if (!string.IsNullOrWhiteSpace(pad.FilePath))
                _ = PreloadPadAsync(pad.Index, cancellationToken);
        }

        PadsChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Soundboard settings loaded ({PadCount} pads)", _settings.Pads.Count);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        SoundboardSettings snapshot;
        lock (_sync)
            snapshot = CloneSettings(_settings);

        return _settingsService.SaveSoundboardSettingsAsync(snapshot, cancellationToken);
    }

    public void AssignPad(int index, string filePath, string? label = null)
    {
        ValidateIndex(index);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        if (!Decoding.AudioFileDecoder.IsSupportedFile(filePath))
        {
            throw new NotSupportedException(
                $"Unsupported audio format '{Path.GetExtension(filePath)}'. Supported: MP3, WAV, AIFF, FLAC.");
        }

        string? previousPath;
        lock (_sync)
        {
            var pad = _settings.Pads[index];
            previousPath = pad.FilePath;
            pad.FilePath = filePath;
            if (!string.IsNullOrWhiteSpace(label))
                pad.Label = label;
            else if (string.IsNullOrWhiteSpace(pad.Label) || pad.Label.StartsWith("Pad ", StringComparison.Ordinal))
                pad.Label = Path.GetFileNameWithoutExtension(filePath);
        }

        if (!string.IsNullOrWhiteSpace(previousPath))
            _mixerImpl.ClipCache.Remove(previousPath);

        _ = PreloadPadAsync(index);
        PadsChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Soundboard pad {Index} assigned: {FilePath}", index, filePath);
    }

    public void ClearPad(int index)
    {
        ValidateIndex(index);

        string? previousPath;
        lock (_sync)
        {
            var pad = _settings.Pads[index];
            previousPath = pad.FilePath;
            pad.FilePath = null;
            pad.Label = $"Pad {index + 1}";
        }

        if (!string.IsNullOrWhiteSpace(previousPath))
            _mixerImpl.ClipCache.Remove(previousPath);

        PadsChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Soundboard pad {Index} cleared", index);
    }

    public void SetPadLabel(int index, string label)
    {
        ValidateIndex(index);

        lock (_sync)
            _settings.Pads[index].Label = label;

        PadsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPadHotkey(int index, string? hotkey)
    {
        ValidateIndex(index);

        lock (_sync)
            _settings.Pads[index].Hotkey = hotkey;

        PadsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayPad(int index)
    {
        ValidateIndex(index);

        string? filePath;
        lock (_sync)
            filePath = _settings.Pads[index].FilePath;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogDebug("PlayPad {Index}: no file assigned", index);
            return;
        }

        if (!_teamSpeak.Client.Connected)
        {
            _logger.LogWarning("Cannot play soundboard pad — not connected to TeamSpeak");
            return;
        }

        var path = filePath;
        Task.Run(() =>
        {
            try
            {
                _mixer.PlaySoundEffect(path);
                PadTriggered?.Invoke(this, index);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play soundboard pad {Index}: {FilePath}", index, path);
            }
        });

        _logger.LogDebug("Soundboard pad {Index} triggered: {FilePath}", index, filePath);
    }

    public int? FindPadByHotkey(string hotkey)
    {
        lock (_sync)
        {
            for (var i = 0; i < _settings.Pads.Count; i++)
            {
                var padHotkey = _settings.Pads[i].Hotkey;
                if (!string.IsNullOrWhiteSpace(padHotkey)
                    && string.Equals(padHotkey, hotkey, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return null;
    }

    public async Task PreloadPadAsync(int index, CancellationToken cancellationToken = default)
    {
        ValidateIndex(index);

        string? filePath;
        lock (_sync)
            filePath = _settings.Pads[index].FilePath;

        if (string.IsNullOrWhiteSpace(filePath))
            return;

        var path = filePath;
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_mixerImpl.ClipCache.Get(path) is not null)
                return;

            var clip = PreloadedClip.TryLoad(path);
            if (clip is null)
            {
                _logger.LogDebug("Preload skipped for {FilePath} (too long or decode failed)", path);
                return;
            }

            _mixerImpl.ClipCache.Set(path, clip);
            _logger.LogInformation("Preloaded soundboard clip: {FilePath} ({SampleCount} samples)", path, clip.SampleCount);
        }, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateIndex(int index)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _settings.Pads.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private static SoundboardSettings CloneSettings(SoundboardSettings source) =>
        new()
        {
            SoundboardVolumeHuman = source.SoundboardVolumeHuman,
            Pads = source.Pads.Select(p => new SoundboardPad
            {
                Index = p.Index,
                Label = p.Label,
                FilePath = p.FilePath,
                Hotkey = p.Hotkey
            }).ToList()
        };
}
