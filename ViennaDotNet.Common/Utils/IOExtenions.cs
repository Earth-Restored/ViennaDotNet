using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Common.Utils
{
    public static class IOExtenions
    {
        public static bool CanRead(this DirectoryInfo dirInfo)
        {
            // TODO: implement
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return true;

            return true;
        }

        public static DirectoryInfo? GetParentFile(this FileInfo info)
        {
            return Directory.GetParent(Path.GetDirectoryName(info.FullName)!);
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

        public static bool IsDirectory(this ZipArchiveEntry entry)
            => entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') || entry.Name == string.Empty;

        public static bool CanExecute(this FileInfo info)
        {
            // TODO: implement

            try
            {
                if (!info.Exists) return false;

                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
