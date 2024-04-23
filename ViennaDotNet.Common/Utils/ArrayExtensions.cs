using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Common.Utils
{
    public static class ArrayExtensions
    {
        public static T[] CopyOfRange<T>(T[] src, int start, int end)
        {
            int len = end - start;
            T[] dest = new T[len];
            Array.Copy(src, start, dest, 0, len);
            return dest;
        }
    }
}
