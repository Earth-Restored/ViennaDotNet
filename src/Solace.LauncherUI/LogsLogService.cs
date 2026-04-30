using System.Collections.Concurrent;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;

namespace ViennaDotNet.LauncherUI;

public class LogsLogService : ILogEventSink
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<LogEvent>> _logsByComponent = new(StringComparer.Ordinal);

    public event Action? OnLogReceived;

    private const int MaxLogs = 500;

    public void AddExternalLogs(params ReadOnlySpan<LogEvent> logs)
    {
        bool newLogsAdded = false;

        foreach (var log in logs)
        {
            var componentName = log.Properties?.ComponentName ?? "Unknown Component";

            var queue = _logsByComponent.GetOrAdd(componentName, _ => new ConcurrentQueue<LogEvent>());
            queue.Enqueue(log);

            if (queue.Count > MaxLogs)
            {
                queue.TryDequeue(out _);
            }

            newLogsAdded = true;
        }

        if (newLogsAdded)
        {
            OnLogReceived?.Invoke();
        }
    }

    public void Emit(Serilog.Events.LogEvent logEvent)
        => AddExternalLogs(
            new LogEvent
            {
                Timestamp = logEvent.Timestamp.UtcDateTime,
                Level = logEvent.Level.ToString(),
                RenderedMessage = logEvent.RenderMessage(),
                Properties = new LogEventProperties { ComponentName = "Launcher" }
            });

    public IEnumerable<string> GetKnownComponents()
        => _logsByComponent.Keys.OrderBy(k => k);

    public IEnumerable<LogEvent> GetLogsFor(string componentName)
    {
        if (_logsByComponent.TryGetValue(componentName, out var queue))
        {
            return [.. queue]; // Creates a safe snapshot for UI rendering
        }

        return [];
    }
}

public static class LauncherSinkExtensions
{
    public static LoggerConfiguration LogsLogSink(
        this LoggerSinkConfiguration loggerConfiguration,
        LogsLogService service)
        => loggerConfiguration.Sink(service);
}