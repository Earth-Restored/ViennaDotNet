using Serilog;

namespace ViennaDotNet.ObjectStore.Server;

public class Server
{
    private readonly DataStore _dataStore;

    public Server(DataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public string? Store(byte[] data)
    {
        try
        {
            string id = _dataStore.Store(data);
            Log.Information($"Stored new object {id}");
            return id;
        }
        catch (DataStore.DataStoreException ex)
        {
            Log.Error("Could not store object", ex);
            return null;
        }
    }

    public byte[]? Load(string id)
    {
        Log.Information($"Request for object {id}");
        try
        {
            byte[]? data = _dataStore.Load(id);
            if (data is null)
                Log.Information($"Requested object {id} does not exist");

            return data;
        }
        catch (DataStore.DataStoreException ex)
        {
            Log.Error($"Could not load object {id}: {ex}");
            return null;
        }
    }

    public bool Delete(string id)
    {
        Log.Information($"Request to delete object {id}");
        _dataStore.Delete(id);
        return true;
    }
}
