using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Common.Utils
{
    public static class Files
    {
        public static void WalkFileTree(string startPath, FileVisitor visitor)
            => walkFileTree(startPath, visitor, 0);
        private static FileVisitResult walkFileTree(string path, FileVisitor visitor, int depth)
        {
            FileVisitResult result = visitor.PreVisitDirectory != null ? visitor.PreVisitDirectory(path) : FileVisitResult.CONTINUE;
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
                    result = visitor.VisitFile != null ? visitor.VisitFile(file) : FileVisitResult.CONTINUE;
                    if (result != FileVisitResult.CONTINUE)
                        return result;
                }
            }
            catch (IOException ex)
            {
                result = visitor.VisitFileFailed != null ? visitor.VisitFileFailed(path, ex) : FileVisitResult.CONTINUE;
                if (result != FileVisitResult.CONTINUE)
                    return result;
            }

            try
            {
                foreach (string subdir in Directory.GetDirectories(path))
                {
                    result = walkFileTree(subdir, visitor, depth + 1);
                    if (result == FileVisitResult.SKIP_SIBLINGS)
                        return FileVisitResult.CONTINUE;
                    else if (result != FileVisitResult.CONTINUE)
                        return result;
                }
            }
            catch (IOException ex)
            {
                result = visitor.PostVisitDirectory != null ? visitor.PostVisitDirectory(path, ex) : FileVisitResult.CONTINUE;
                if (result != FileVisitResult.CONTINUE)
                    return result;
            }

            return visitor.PostVisitDirectory != null ? visitor.PostVisitDirectory(path, null) : FileVisitResult.CONTINUE;
        } 
    }
}
