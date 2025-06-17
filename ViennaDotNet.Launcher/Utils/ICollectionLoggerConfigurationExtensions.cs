using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Parsing;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Launcher.Utils;

internal static class ICollectionLoggerConfigurationExtensions
{
    internal static readonly object DefaultSyncRoot = new object();

    internal const string DefaultOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static LoggerConfiguration Collection(
         this LoggerSinkConfiguration sinkConfiguration,
         ICollection<string> logCollection,
         string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
         LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
         IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sinkConfiguration);
        ArgumentNullException.ThrowIfNull(logCollection);

        var sink = new CollectionSink(logCollection, outputTemplate, restrictedToMinimumLevel, formatProvider);
        return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
    }
}
