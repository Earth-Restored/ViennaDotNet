using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.PreviewGenerator.Utils.NBT
{
    public class NbtMapBuilder : IDictionary<string, object>
    {
        public static NbtMapBuilder from(NbtMap map)
        {
            NbtMapBuilder builder = new NbtMapBuilder();
            builder.map.AddRange(map.map);
            return builder;
        }

        private readonly Dictionary<string, object> map = new Dictionary<string, object>();

        public object this[string key] { get => map[key]; set => map[key] = value; }

        public ICollection<string> Keys => map.Keys;

        public ICollection<object> Values => map.Values;

        public int Count => map.Count;

        public bool IsReadOnly => false;

        public void Add(string key, object value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));

            if (value is bool b)
            {
                value = (byte)(b ? 1 : 0);
            }

            NbtType.byClass(value.GetType()); // Make sure value is valid
            this[key] = NbtUtils.copy(value);
        }
        public object put(string key, object value)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));

            if (value is bool b)
            {
                value = (byte)(b ? 1 : 0);
            }

            NbtType.byClass(value.GetType()); // Make sure value is valid
            object val = NbtUtils.copy(value);
            this[key] = val;
            return val;
        }

        public void Add(KeyValuePair<string, object> item)
        {
            throw new InvalidOperationException();
        }

        public void Clear()
            => map.Clear();

        public bool Contains(KeyValuePair<string, object> item)
            => map.Contains(item);

        public bool ContainsKey(string key)
            => map.ContainsKey(key);

        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
            => map.GetEnumerator();

        public bool Remove(string key)
            => map.Remove(key);

        public bool Remove(KeyValuePair<string, object> item)
            => throw new InvalidOperationException();

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out object value)
            => map.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator()
            => map.GetEnumerator();

        public NbtMapBuilder putBoolean(String name, bool value)
        {
            Add(name, (byte)(value ? 1 : 0));
            return this;
        }

        public NbtMapBuilder putByte(String name, byte value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putByteArray(String name, byte[] value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putDouble(String name, double value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putFloat(String name, float value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putIntArray(String name, int[] value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putLongArray(String name, long[] value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putInt(String name, int value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putLong(String name, long value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putShort(String name, short value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putString(String name, String value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putCompound(String name, NbtMap value)
        {
            Add(name, value);
            return this;
        }

        public NbtMapBuilder putList(string name, NbtType type, params object[] values)
        {
            Add(name, new NbtList(type, values));
            return this;
        }

        public NbtMapBuilder putList(string name, NbtType type, IList list)
        {
            if (list is not NbtList)
                list = new NbtList(type, list);

            Add(name, list);
            return this;
        }

        public NbtMapBuilder rename(String oldName, String newName)
        {
            if (TryGetValue(oldName, out object? o))
            {
                Remove(oldName);
                Add(newName, o);
            }

            return this;
        }

        public NbtMap build()
        {
            if (Count == 0)
                return NbtMap.EMPTY;

            return new NbtMap(this);
        }

        public override string ToString()
        {
            return NbtMap.mapToString(this);
        }
    }
}
