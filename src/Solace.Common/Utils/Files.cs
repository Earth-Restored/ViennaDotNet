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
    }
}
