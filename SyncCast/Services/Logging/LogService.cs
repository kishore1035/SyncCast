using System.Collections.ObjectModel;
using SyncCast.Models;

namespace SyncCast.Services.Logging;

public interface ILogService
{
    ObservableCollection<LogMessage> Logs { get; }
    void Log(string message, LogLevel level = LogLevel.Info);
    void LogInfo(string message) => Log(message, LogLevel.Info);
    void LogSuccess(string message) => Log(message, LogLevel.Success);
    void LogWarning(string message) => Log(message, LogLevel.Warning);
    void LogError(string message) => Log(message, LogLevel.Error);
    void Clear();
}

public class LogService : ILogService
{
    private readonly SynchronizationContext? _syncContext;
    public ObservableCollection<LogMessage> Logs { get; } = new();

    public LogService()
    {
        _syncContext = SynchronizationContext.Current;
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var item = new LogMessage
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };

        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => AppendLog(item), null);
        }
        else
        {
            AppendLog(item);
        }
    }

    private void AppendLog(LogMessage item)
    {
        Logs.Add(item);
        // Retain latest 300 entries to prevent memory growth
        while (Logs.Count > 300)
        {
            Logs.RemoveAt(0);
        }
    }

    public void Clear()
    {
        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => Logs.Clear(), null);
        }
        else
        {
            Logs.Clear();
        }
    }
}
