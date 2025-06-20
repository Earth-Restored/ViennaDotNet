using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using System;
using System.Text;

namespace ViennaDotNet.Launcher.Utils;

#pragma warning disable CA1001 // Types that own disposable fields should be disposable - StringWriter does not need to be disposed
internal sealed class CollectionSink : ILogEventSink
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
{
    private readonly ICollection<string> _logCollection;
    private readonly LogEventLevel _minLevel;
    private readonly MessageTemplateTextFormatter _formatter;
    private readonly StringWriter _writer = new(new StringBuilder());

    public CollectionSink(
        ICollection<string> logCollection,
        string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        LogEventLevel minLevel = LogEventLevel.Verbose,
        IFormatProvider? formatProvider = null)
    {
        _logCollection = logCollection ?? throw new ArgumentNullException(nameof(logCollection));
        _minLevel = minLevel;
        _formatter = new MessageTemplateTextFormatter(outputTemplate, formatProvider);
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (logEvent.Level < _minLevel)
        {
            return;
        }

        _formatter.Format(logEvent, _writer);
        var builder = _writer.GetStringBuilder();
#if NET9_0_OR_GREATER
        var log = builder.ToString().AsSpan();

        foreach (var lineRange in log.Split(Environment.NewLine))
        {
            var line = log[lineRange].Trim();

            if (!line.IsEmpty)
            {
                _logCollection.Add(line.ToString());
            }
        }
#else
        string log = builder.ToString();

        foreach (string line in log.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            _logCollection.Add(line.ToString());
        }
#endif

        builder.Clear();
    }
}