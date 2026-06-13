using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface ISoundboardService
{
    IReadOnlyList<SoundboardPad> Pads { get; }
    float Volume { get; set; }

    event EventHandler? PadsChanged;
    event EventHandler<int>? PadTriggered;

    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);

    void AssignPad(int index, string filePath, string? label = null);
    void ClearPad(int index);
    void SetPadLabel(int index, string label);
    void SetPadHotkey(int index, string? hotkey);
    void PlayPad(int index);
    int? FindPadByHotkey(string hotkey);
    Task PreloadPadAsync(int index, CancellationToken cancellationToken = default);
}
