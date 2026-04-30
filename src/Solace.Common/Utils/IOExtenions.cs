using System.IO.Compression;

namespace ViennaDotNet.Common.Utils;

public static class IOExtenions
{
    public static bool CanRead(this DirectoryInfo dirInfo)
    {
        // TODO: implement
        if (!dirInfo.Exists) return false;

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return true;
        }

        return true;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="info"></param>
    /// <returns>If the directory was created</returns>
    public static bool TryCreate(this DirectoryInfo info)
    {
        try
        {
            info.Create();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    extension (ZipArchiveEntry entry)
    {
        public bool IsDirectory => entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || entry.Name == string.Empty;
    }

    public static bool CanExecute(this FileInfo info)
    {
        // TODO: implement

        try
        {
            if (!info.Exists)
            {
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
