namespace TS_DJ.Audio.Mixing;

/// <summary>
/// Thread-safe cache of preloaded soundboard clips keyed by file path.
/// </summary>
public sealed class PreloadedClipCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PreloadedClip> _clips = new(StringComparer.OrdinalIgnoreCase);

    public PreloadedClip? Get(string filePath)
    {
        lock (_sync)
        {
            return _clips.TryGetValue(filePath, out var clip) ? clip : null;
        }
    }

    public void Set(string filePath, PreloadedClip clip)
    {
        lock (_sync)
        {
            if (_clips.TryGetValue(filePath, out var existing))
                existing.Dispose();

            _clips[filePath] = clip;
        }
    }

    public void Remove(string filePath)
    {
        lock (_sync)
        {
            if (_clips.Remove(filePath, out var clip))
                clip.Dispose();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            foreach (var clip in _clips.Values)
                clip.Dispose();

            _clips.Clear();
        }
    }
}
