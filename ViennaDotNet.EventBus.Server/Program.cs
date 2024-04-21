using Serilog.Events;
using Serilog;

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

            ushort port = 5532;

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
