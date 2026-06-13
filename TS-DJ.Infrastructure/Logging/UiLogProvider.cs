using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TS_DJ.Core.Models;
using TS_DJ.Core.Services;

namespace TS_DJ.Infrastructure.Logging;

public sealed class UiLogProvider : ILoggerProvider
{
    private readonly ILogService _logService;
    private readonly ConcurrentDictionary<string, UiLogger> _loggers = new();

    public UiLogProvider(ILogService logService)
    {
        _logService = logService;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new UiLogger(name, _logService));

    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class UiLogger : ILogger
    {
        private readonly string _category;
        private readonly ILogService _logService;

        public UiLogger(string category, ILogService logService)
        {
            _category = category;
            _logService = logService;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception is not null)
                message = $"{message} ({exception.Message})";

            _logService.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = logLevel.ToString(),
                Message = string.IsNullOrEmpty(_category) ? message : $"[{ShortCategory(_category)}] {message}"
            });
        }

        private static string ShortCategory(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot >= 0 ? category[(lastDot + 1)..] : category;
        }
    }
}
