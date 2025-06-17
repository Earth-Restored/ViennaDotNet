using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        _logCollection.Add(builder.ToString());
        builder.Clear();
    }
}