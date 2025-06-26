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

    public string Store(byte[] data)
    {
        string id = U.RandomUuid().ToString();

        DirectoryInfo dir = new DirectoryInfo(Path.Combine(_rootDirectory.FullName, id[..2]));
        if (!dir.Exists)
        {
            dir.Create();
        }

        FileInfo file = new FileInfo(Path.Combine(dir.FullName, id));

        try
        {
            using (FileStream fileOutputStream = file.OpenWrite())
            {
                fileOutputStream.Write(data);
            }
        }
        catch (IOException ex)
        {
            file.Delete();
            throw new DataStoreException(ex);
        }

        return id;
    }

    public byte[]? Load(string id)
    {
        FileInfo file = new FileInfo(Path.Combine(_rootDirectory.FullName, id[..2], id));
        if (!file.Exists)
        {
            return null;
        }

        MemoryStream byteArrayOutputStream;
        try
        {
            byteArrayOutputStream = new MemoryStream((int)file.Length);
        }
        catch (IOException ex)
        {
            throw new DataStoreException(ex);
        }

        try
        {
            using (FileStream fileInputStream = file.OpenRead())
                fileInputStream.CopyTo(byteArrayOutputStream);
        }

        catch (IOException ex)
        {
            throw new DataStoreException(ex);
        }

        byte[] data = byteArrayOutputStream.ToArray();

        return data;
    }

    public void Delete(string id)
    {
        FileInfo file = new FileInfo(Path.Combine(_rootDirectory.FullName, id.Substring(0, 2), id));
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
