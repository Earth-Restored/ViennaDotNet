using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.PreviewGenerator.Utils.NBT
{
    public class NbtList : IList
    {
        public static readonly NbtList EMPTY = new NbtList(NbtType.END);

        private readonly NbtType type;
        private readonly Array array;
        [JsonIgnore]
        private bool hashCodeGenerated;
        [JsonIgnore]
        private int hashCode;

        public bool IsFixedSize => true;

        public bool IsReadOnly => true;

        public int Count => array.Length;

        public bool IsSynchronized => false;

        public object SyncRoot => null!;

        public object? this[int index] { get => this.get(index); set => throw new InvalidOperationException(); }

        public NbtList(NbtType type, ICollection collection)
        {
            ArgumentNullException.ThrowIfNull(type, "tagClass");
            this.type = type;
            array = Array.CreateInstance(type.getTagClass(), collection.Count);
            collection.CopyTo(array, 0);
        }

        public NbtList(NbtType tagClass, params object[] array)
        {
            ArgumentNullException.ThrowIfNull(type, "tagClass");
            this.type = tagClass;
            this.array = (Array)array.Clone();
        }

        public NbtType getType()
        {
            return type;
        }

        public object get(int index)
        {
            if (index < 0 || index >= array.Length)
                throw new IndexOutOfRangeException("Expected 0-" + (array.Length - 1) + ". Got " + index);

            return NbtUtils.copy(array.GetValue(index)!);
        }

        public int size()
        {
            return array.Length;
        }

        public int Add(object? value)
        {
            throw new InvalidOperationException();
        }

        public void Clear()
        {
            throw new InvalidOperationException();
        }

        public bool Contains(object? value)
        {
            return Array.IndexOf(array, value) >= 0;
        }

        public int IndexOf(object? value)
        {
            return Array.IndexOf(array, value);
        }

        public void Insert(int index, object? value)
        {
            throw new InvalidOperationException();
        }

        public void Remove(object? value)
        {
            throw new InvalidOperationException();
        }

        public void RemoveAt(int index)
        {
            throw new InvalidOperationException();
        }

        public void CopyTo(Array array, int index)
        {
            this.array.CopyTo(array, index);
        }

        public IEnumerator GetEnumerator()
        {
            return array.GetEnumerator();
        }
    }
}
