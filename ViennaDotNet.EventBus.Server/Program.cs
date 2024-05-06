using CommandLine;
using Serilog;

namespace ViennaDotNet.EventBus.Server
{
    internal static class Program
    {
        class Options
        {
            [Option("port", Default = 5532, Required = false, HelpText = "Port to listen on")]
            public int Port { get; set; }
        }
        static int Main(string[] args)
        {
            var log = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/debug.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .CreateLogger();

            Log.Logger = log;

            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Log.Fatal($"Unhandeled exception: {e.ExceptionObject}");
                Environment.Exit(1);
            };

            ParserResult<Options> res = Parser.Default.ParseArguments<Options>(args);

            Options options;
            if (res is Parsed<Options> parsed)
                options = parsed.Value;
            else if (res is NotParsed<Options> notParsed)
            {
                if (res.Errors.Any(error => error is HelpRequestedError))
                    return 0;
                else if (res.Errors.Any(error => error is VersionRequestedError))
                    return 0;
                else
                    return 1;
            }
            else
                return 1;

            NetworkServer server;
            try
            {
                server = new NetworkServer(new Server(), options.Port);
            }
            catch (IOException ex)
            {
                Log.Fatal(ex.ToString());
                return 1;
            }

            server.run();

            return 0;
        }
    }
}
