using Serilog;

namespace ViennaDotNet.ObjectStore.Server;

public class Server
{
    private readonly DataStore _dataStore;

    public Server(DataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public async Task<string?> StoreAsync(byte[] data)
    {
        try
        {
            string id = await _dataStore.StoreAsync(data);
            Log.Information($"Stored new object {id}");
            return id;
        }
        catch (DataStore.DataStoreException ex)
        {
            Log.Error("Could not store object", ex);
            return null;
        }
    }

    public async Task<byte[]?> LoadAsync(string id)
    {
        Log.Information($"Request for object {id}");
        try
        {
            byte[]? data = await _dataStore.LoadAsync(id);
            if (data is null)
            {
                Log.Information($"Requested object {id} does not exist");
            }

            return data;
        }
        catch (DataStore.DataStoreException ex)
        {
            Log.Error($"Could not load object {id}: {ex}");
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        Log.Information($"Request to delete object {id}");
        await _dataStore.DeleteAsync(id);
        return true;
    }
}
