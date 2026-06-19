using System.Collections.ObjectModel;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Logging;

public sealed class LogService : ILogService
{
    public const int MaxLogEntries = 1000;

    private readonly List<LogEntry> _entries = [];
    private readonly object _lock = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return new ReadOnlyCollection<LogEntry>(_entries.ToList());
            }
        }
    }

    public event EventHandler<LogEntry>? EntryAdded;

    public void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxLogEntries)
                _entries.RemoveAt(0);
        }

        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }
}
