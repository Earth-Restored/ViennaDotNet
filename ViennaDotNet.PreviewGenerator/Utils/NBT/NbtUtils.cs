using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.PreviewGenerator.Utils.NBT
{
    public static class NbtUtils
    {
        public static readonly int MAX_DEPTH = 16;
        public static readonly long MAX_READ_SIZE = 0; // Disabled by default

        public static T copy<T>(T val)
        {
            if (val is byte[] bytes)
                return (T)bytes.Clone();
            else if (val is int[] ints)
                return (T)ints.Clone();
            else if (val is long[] longs)
                return (T)longs.Clone();

            return val;
        }
    }
}
