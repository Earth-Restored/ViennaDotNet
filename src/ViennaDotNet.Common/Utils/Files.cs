namespace ViennaDotNet.Common.Utils;

public static class Files
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

    public static void WalkFileTree(string startPath, FileVisitor visitor)
        => WalkFileTree(startPath, visitor, 0);

    private static FileVisitResult WalkFileTree(string path, FileVisitor visitor, int depth)
    {
        FileVisitResult result = visitor.PreVisitDirectory is not null ? visitor.PreVisitDirectory(path) : FileVisitResult.CONTINUE;
        if (result != FileVisitResult.CONTINUE)
        {
            if (result == FileVisitResult.SKIP_SUBTREE)
                return FileVisitResult.CONTINUE;
            else
                return result;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(path))
            {
                result = visitor.VisitFile is not null ? visitor.VisitFile(file) : FileVisitResult.CONTINUE;
                if (result != FileVisitResult.CONTINUE)
                    return result;
            }
        }
        catch (IOException ex)
        {
            result = visitor.VisitFileFailed is not null ? visitor.VisitFileFailed(path, ex) : FileVisitResult.CONTINUE;
            if (result != FileVisitResult.CONTINUE)
                return result;
        }

        try
        {
            foreach (string subdir in Directory.GetDirectories(path))
            {
                result = WalkFileTree(subdir, visitor, depth + 1);
                if (result == FileVisitResult.SKIP_SIBLINGS)
                    return FileVisitResult.CONTINUE;
                else if (result != FileVisitResult.CONTINUE)
                    return result;
            }
        }
        catch (IOException ex)
        {
            result = visitor.PostVisitDirectory is not null ? visitor.PostVisitDirectory(path, ex) : FileVisitResult.CONTINUE;
            if (result != FileVisitResult.CONTINUE)
                return result;
        }

        return visitor.PostVisitDirectory is not null ? visitor.PostVisitDirectory(path, null) : FileVisitResult.CONTINUE;
    }
}
