namespace ViennaDotNet.Common.Utils;

public class FileVisitor
{
    public Func<string, FileVisitResult>? PreVisitDirectory;
    public Func<string, FileVisitResult>? VisitFile;
    public Func<string, IOException?, FileVisitResult>? VisitFileFailed;
    public Func<string, IOException?, FileVisitResult>? PostVisitDirectory;

    public FileVisitor(Func<string, FileVisitResult>? _preVisitDirectory, Func<string, FileVisitResult>? _visitFile, Func<string, IOException?, FileVisitResult>? _visitFileFailed, Func<string, IOException?, FileVisitResult>? _postVisitDirectory)
    {
        PreVisitDirectory = _preVisitDirectory;
        VisitFile = _visitFile;
        VisitFileFailed = _visitFileFailed;
        PostVisitDirectory = _postVisitDirectory;
    }
}
