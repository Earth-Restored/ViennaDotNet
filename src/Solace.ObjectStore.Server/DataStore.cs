using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.ObjectStore.Server;

public class DataStore
{
    private readonly DirectoryInfo _rootDirectory;

    public DataStore(DirectoryInfo rootDirectory)
    {
        _rootDirectory = rootDirectory;

        if (!_rootDirectory.Exists)
        {
            _rootDirectory.Create();
        }
    }

    public async Task<string> StoreAsync(byte[] data)
    {
        string id = U.RandomUuid().ToString();

        var dir = new DirectoryInfo(Path.Combine(_rootDirectory.FullName, id[..2]));
        if (!dir.Exists)
        {
            dir.Create();
        }

        var file = new FileInfo(Path.Combine(dir.FullName, id));

        try
        {
            await File.WriteAllBytesAsync(file.FullName, data);
        }
        catch (IOException ex)
        {
            file.Delete();
            throw new DataStoreException(ex);
        }

        return id;
    }

    public async Task<byte[]?> LoadAsync(string id)
    {
        var file = new FileInfo(Path.Combine(_rootDirectory.FullName, id[..2], id));
        if (!file.Exists)
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(file.FullName);
        }
        catch (IOException ex)
        {
            throw new DataStoreException(ex);
        }
    }

    public async Task DeleteAsync(string id)
    {
        var file = new FileInfo(Path.Combine(_rootDirectory.FullName, id[..2], id));
        file.Delete();
    }

    public class DataStoreException : Exception
    {
        public DataStoreException(string? message)
            : base(message)
        {
        }

        public DataStoreException(Exception? cause)
            : base(null, cause)
        {
        }
    }
}
