namespace ViennaDotNet.Common.Utils;

public static class DirectoryInfoExtensions
{
    extension(DirectoryInfo directoryInfo)
    {
        public void CopyTo(string destDirectoryName, bool recursive = true)
        {
            if (!directoryInfo.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory not found: {directoryInfo.FullName}");
            }

            DirectoryInfo[] subDirs = directoryInfo.GetDirectories();

            Directory.CreateDirectory(destDirectoryName);

            foreach (FileInfo file in directoryInfo.GetFiles())
            {
                string targetFilePath = Path.Combine(destDirectoryName, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            if (recursive)
            {
                foreach (DirectoryInfo subDir in subDirs)
                {
                    string newDestDir = Path.Combine(destDirectoryName, subDir.Name);
                    subDir.CopyTo(newDestDir, true);
                }
            }
        }
    }
}