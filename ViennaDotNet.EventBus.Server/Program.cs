using Serilog.Events;
using Serilog;
using CliUtils.Exceptions;
using CliUtils;

namespace ViennaDotNet.EventBus.Server
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .CreateLogger();

            Log.Logger = log;

            Options options = new Options();
            options.addOption(Option.builder()
                .Option("port")
                .LongOpt("port")
                .HasArg()
                .ArgName("port")
                .Desc("Port to listen on, defaults to 5532")
                .Build());
            CommandLine commandLine;
            int port;
            try
            {
                commandLine = new DefaultParser().parse(options, args);
                port = commandLine.hasOption("port") ? commandLine.getParsedOptionValue<int>("port") : 5532;
            }
            catch (ParseException exception)
            {
                Log.Fatal(exception.ToString());
                Environment.Exit(1);
                return;
            }

            NetworkServer server;
            try
            {
                server = new NetworkServer(new Server(), port);
            }
            catch (IOException exception)
            {
                Log.Fatal(string.Empty, exception);
                Environment.Exit(1);
                return;
            }

            server.run();
        }
    }
}
