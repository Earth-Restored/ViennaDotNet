using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;

namespace ViennaDotNet.Buildplate.Launcher
{
    public class Starter
    {
        private readonly EarthDB earthDB;
        private readonly ObjectStoreClient objectStoreClient;
        private readonly EventBusClient eventBusClient;

        private readonly string publicAddress;
        private readonly string javaCmd;
        private readonly DirectoryInfo tmpDir;
        private readonly string eventBusConnectionString;
        private readonly string apiServerAddress;
        private readonly string apiServerToken;

        private readonly FileInfo fountainBridgeJar;
        private readonly DirectoryInfo serverTemplateDir;
        private readonly string fabricJarName;
        private readonly FileInfo connectorPluginJar;

        // TODO: const ?
        private static readonly int BASE_PORT = 19132;
        private static readonly int SERVER_INTERNAL_BASE_PORT = 25565;
        private readonly HashSet<int> portsInUse = new HashSet<int>();
        private readonly HashSet<int> serverInternalPortsInUse = new HashSet<int>();

        public Starter(EarthDB earthDB, ObjectStoreClient objectStoreClient, EventBusClient eventBusClient, string eventBusConnectionString, string apiServerAddress, string apiServerToken, string publicAddress, string bridgeJar, string serverTemplateDir, string fabricJarName, string connectorPluginJar)
        {
            this.earthDB = earthDB;
            this.objectStoreClient = objectStoreClient;
            this.eventBusClient = eventBusClient;

            this.publicAddress = publicAddress;
            this.javaCmd = locateJava();
            this.tmpDir = new DirectoryInfo(Path.GetTempPath());
            this.eventBusConnectionString = eventBusConnectionString;
            this.apiServerAddress = apiServerAddress;
            this.apiServerToken = apiServerToken;

            this.fountainBridgeJar = new FileInfo(bridgeJar);
            this.serverTemplateDir = new DirectoryInfo(serverTemplateDir);
            this.fabricJarName = fabricJarName;
            this.connectorPluginJar = new FileInfo(connectorPluginJar);
        }

        public Instance? startInstance(string instanceId, string playerId, string buildplateId, bool survival, bool night)
        {
            DirectoryInfo? baseDir = this.createInstanceBaseDir(instanceId);
            if (baseDir == null)
                return null;

            int port = findPort(portsInUse, BASE_PORT);
            int serverInternalPort = findPort(serverInternalPortsInUse, SERVER_INTERNAL_BASE_PORT);
            Instance instance = Instance.run(earthDB, objectStoreClient, eventBusClient, playerId, buildplateId, instanceId, survival, night, publicAddress, port, serverInternalPort, javaCmd, fountainBridgeJar, serverTemplateDir, fabricJarName, connectorPluginJar, baseDir, eventBusConnectionString, apiServerAddress, apiServerToken);
            new Thread(() =>
            {
                instance.waitForShutdown();
                releasePort(portsInUse, port);
                releasePort(serverInternalPortsInUse, serverInternalPort);
            }).Start();
            return instance;
        }

        private static int findPort(HashSet<int> portsInUse, int basePort)
        {
            lock (portsInUse)
            {
                int port = basePort;
                while (portsInUse.Contains(port))
                    port++;

                portsInUse.Add(port);
                return port;
            }
        }

        private static void releasePort(HashSet<int> portsInUse, int port)
        {
            lock (portsInUse)
            {
                if (!portsInUse.Remove(port))
                    throw new InvalidOperationException();
            }
        }

        private static string locateJava()
        {
            Log.Information("Trying to locate Java");

            string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                Log.Information("Trying JAVA_HOME");
                try
                {
                    FileInfo file = new FileInfo(Path.Combine(javaHome, "bin", "java"));
                    if (file.CanExecute())
                    {
                        string path = file.FullName;
                        Log.Information($"Using Java from JAVA_HOME ({path})");
                        return path;
                    }
                    file = new FileInfo(Path.Combine(javaHome, "bin", "java.exe"));
                    if (file.CanExecute())
                    {
                        string path = file.FullName;
                        Log.Information($"Using Java from JAVA_HOME ({path})");
                        return path;
                    }
                }
                catch (IOException)
                {
                    // empty
                }
                Log.Information("Java from JAVA_HOME is not suitable (does not exist or cannot be accessed)");
            }
            else
                Log.Information("JAVA_HOME is not set");

            Log.Information("Using \"java\"");
            return "java";
        }

        private DirectoryInfo? createInstanceBaseDir(string instanceId)
        {
            DirectoryInfo file = new DirectoryInfo(Path.Combine(tmpDir.FullName, $"vienna-buildplate-instance_{instanceId}"));
            if (!file.TryCreate())
            {
                Log.Error($"Error creating instance base directory for {instanceId}");
                return null;
            }
            Log.Debug($"Created instance base directory {file.FullName}");
            return file;
        }
    }
}
