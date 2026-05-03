namespace Solace.Common.Utils;

#pragma warning disable CA1708 // Identifiers should differ by more than case
public static class Files
#pragma warning restore CA1708 // Identifiers should differ by more than case
{
    extension(File)
    {
        public static FileStream OpenWriteNew(string path)
            => File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    extension(FileInfo file)
    {
        public FileStream OpenWriteNew()
           => File.Open(file.FullName, FileMode.Create, FileAccess.Write, FileShare.Read);

        public long SafeLength
        {
            get
            {
                if (!file.Exists)
                {
                    return 0;
                }

                return file.Length;
            }
        }
    }

    extension(DirectoryInfo directory)
    {
        public long Length => directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);

        public long SafeLength
        {
            get
            {
                if (!directory.Exists)
                {
                    return 0;
                }

                return directory.Length;
            }
        }
    }
}
