using TS_DJ.Core.Models;

namespace TS_DJ.Core.Services;

public interface ILogService
{
    IReadOnlyList<LogEntry> Entries { get; }

    event EventHandler<LogEntry>? EntryAdded;

    void Add(LogEntry entry);
    void Clear();
}
