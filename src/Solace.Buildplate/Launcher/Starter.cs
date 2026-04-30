using System.Diagnostics;
using Serilog;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.EventBus.Client;

namespace ViennaDotNet.Buildplate.Launcher;

public sealed class Starter
{
    private readonly EventBusClient _eventBusClient;

	private readonly string _publicAddress;
	private readonly string _javaCmd;
	private readonly DirectoryInfo _tmpDir;
	private readonly string _eventBusConnectionString;

	private readonly FileInfo _fountainBridgeJar;
	private readonly DirectoryInfo _serverTemplateDir;
	private readonly String _fabricJarName;
	private readonly FileInfo _connectorPluginJar;

	private const ushort BASE_PORT = 19132;
	private const ushort SERVER_INTERNAL_BASE_PORT = 25565;
	private readonly HashSet<int> _portsInUse = [];
	private readonly HashSet<int> _serverInternalPortsInUse = [];

    public Starter(EventBusClient eventBusClient, string eventBusConnectionString, string publicAddress, string javaCmd, string bridgeJar, string serverTemplateDir, string fabricJarName, string connectorPluginJar)
	{
		_eventBusClient = eventBusClient;

		_publicAddress = publicAddress;
		_javaCmd = javaCmd;
		_tmpDir = new DirectoryInfo(Path.GetTempPath());
		_eventBusConnectionString = eventBusConnectionString;

		_fountainBridgeJar = new FileInfo(Path.GetFullPath(bridgeJar));
		_serverTemplateDir = new DirectoryInfo(Path.GetFullPath(serverTemplateDir));
		_fabricJarName = fabricJarName;
		_connectorPluginJar = new FileInfo(connectorPluginJar);
	}

    public Instance? StartInstance(string instanceId, string? playerId, string buildplateId, Instance.BuildplateSource buildplateSource, bool survival, bool night, bool saveEnabled, InventoryType inventoryType, long? shutdownTime)
	{
		DirectoryInfo? baseDir = CreateInstanceBaseDir(instanceId);
		if (baseDir is null)
		{
			return null;
		}

		int port = FindPort(_portsInUse, BASE_PORT);
		int serverInternalPort = FindPort(_serverInternalPortsInUse, SERVER_INTERNAL_BASE_PORT);
		var instance = Instance.Run(_eventBusClient, playerId, buildplateId, buildplateSource, instanceId, survival, night, saveEnabled, inventoryType, shutdownTime, _publicAddress, port, serverInternalPort, _javaCmd, _fountainBridgeJar, _serverTemplateDir, _fabricJarName, _connectorPluginJar, baseDir, _eventBusConnectionString);

        Task.Run(async () =>
        {
            await instance.WaitForShutdownAsync();
			ReleasePort(_portsInUse, port);
			ReleasePort(_serverInternalPortsInUse, serverInternalPort);
        }).Forget();
        
		return instance;
	}

    private static int FindPort(HashSet<int> portsInUse, int basePort)
	{
		lock (portsInUse)
		{
			int port = basePort;
			while (portsInUse.Contains(port))
			{
				port++;
			}

			portsInUse.Add(port);
			return port;
		}
	}

	private static void ReleasePort(HashSet<int> portsInUse, int port)
	{
		lock (portsInUse)
		{
			if (!portsInUse.Remove(port))
			{
				throw new UnreachableException();
			}
		}
	}

    private DirectoryInfo? CreateInstanceBaseDir(string instanceId)
	{
		var file = new DirectoryInfo(Path.Combine(_tmpDir.FullName, $"vienna-buildplate-instance_{instanceId}"));
		if (!file.TryCreate())
		{
			Log.Error($"Error creating instance base directory for {instanceId}");
			return null;
		}

		Log.Debug($"Created instance base directory {file.FullName}");
		return file;
	}
}