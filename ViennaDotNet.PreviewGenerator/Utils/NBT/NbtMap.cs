using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.PreviewGenerator.Utils.NBT
{
    public class NbtMap// : IDictionary<string, object>
    {
        public static readonly NbtMap EMPTY = new NbtMap();

        private static readonly byte[] EMPTY_BYTE_ARRAY = new byte[0];
        private static readonly int[] EMPTY_INT_ARRAY = new int[0];
        private static readonly long[] EMPTY_LONG_ARRAY = new long[0];

        internal readonly IDictionary<string, object> map;

        public int Count => map.Count;

        [JsonIgnore]
        private ICollection<string> _keySet;
        [JsonIgnore]
        private ICollection<KeyValuePair<string, object>> _entrySet;
        [JsonIgnore]
        private ICollection<object> _values;
        [JsonIgnore]
        private bool hashCodeGenerated;
        [JsonIgnore]
        private int hashCode;

        private NbtMap()
        {
            map = new Dictionary<string, object>();
        }

        internal NbtMap(IDictionary<string, object> map)
        {
            this.map = map;
        }

        public static NbtMapBuilder builder()
        {
            return new NbtMapBuilder();
        }

        public static NbtMap fromMap(IDictionary<string, object> map)
        {
            return new NbtMap(map.AsReadOnly());
        }

        public NbtMapBuilder toBuilder()
        {
            return NbtMapBuilder.from(this);
        }

        public bool containsKey(string key)
            => map.ContainsKey(key);
        public bool containsKey(string key, NbtType type)
        {
            if (map.TryGetValue(key, out object? o))
                return o.GetType() == type.getTagClass();
            else
                return false;
        }

        public object get(object key)
        {
            return NbtUtils.copy(map.GetOrDefault(key));
        }

        public ICollection<string> keySet()
        {
            if (_keySet == null) _keySet = map.Keys;
            return _keySet;
        }

        public ICollection<KeyValuePair<string, object>> entrySet()
        {
            if (_entrySet == null) _entrySet = map;
            return _entrySet;
        }

        public ICollection<object> values()
        {
            if (_values == null) _values = map.Values;
            return _values;
        }

        public bool getbool(string key)
        {
            return getbool(key, false);
        }

        public bool getbool(string key, bool defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is byte b)
                return b != 0;

            return defaultValue;
        }

        public void listenForbool(string key, Action<bool> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is byte b)
                consumer.Invoke(b != 0);
        }

        public byte getByte(string key)
        {
            return getByte(key, 0);
        }

        public byte getByte(string key, byte defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is byte b)
                return b;

            return defaultValue;
        }

        public void listenForByte(string key, Action<byte> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is byte b)
                consumer.Invoke(b);
        }

        public short getShort(string key)
        {
            return getShort(key, 0);
        }

        public short getShort(string key, short defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is short s)
                return s;

            return defaultValue;
        }

        public void listenForShort(string key, Action<short> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is short s)
                consumer.Invoke(s);
        }

        public int getInt(string key)
        {
            return getInt(key, 0);
        }

        public int getInt(string key, int defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is int i)
                return i;

            return defaultValue;
        }

        public void listenForInt(string key, Action<int> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is int i)
                consumer.Invoke(i);
        }

        public long getLong(string key)
        {
            return getLong(key, 0L);
        }

        public long getLong(string key, long defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is long l)
                return l;

            return defaultValue;
        }

        public void listenForLong(string key, Action<long> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is long l)
                consumer.Invoke(l);
        }

        public float getFloat(string key)
        {
            return getFloat(key, 0F);
        }

        public float getFloat(string key, float defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is float f)
                return f;

            return defaultValue;
        }

        public void listenForFloat(string key, Action<float> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is float f)
                consumer.Invoke(f);
        }

        public double getDouble(string key)
        {
            return getDouble(key, 0.0);
        }

        public double getDouble(string key, double defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is double d)
                return d;

            return defaultValue;
        }

        public void listenForDouble(string key, Action<double> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is double d)
                consumer.Invoke(d);
        }

        public string? getstring(string key)
        {
            return getstring(key, "");
        }

        public string? getstring(string key, string? defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is string s)
                return s;

            return defaultValue;
        }

        public void listenForstring(string key, Action<string> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is string s)
                consumer.Invoke(s);
        }

        public byte[]? getByteArray(string key)
        {
            return getByteArray(key, EMPTY_BYTE_ARRAY);
        }

        public byte[]? getByteArray(string key, byte[]? defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is byte[] bytes)
                return (byte[])bytes.Clone();

            return defaultValue;
        }

        public void listenForByteArray(string key, Action<byte[]> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is byte[] bytes)
                consumer.Invoke((byte[])bytes.Clone());
        }

        public int[]? getIntArray(string key)
        {
            return getIntArray(key, EMPTY_INT_ARRAY);
        }

        public int[]? getIntArray(string key, int[]? defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is int[] ints)
                return (int[])ints.Clone();

            return defaultValue;
        }

        public void listenForIntArray(string key, Action<int[]> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is int[] ints)
                consumer.Invoke((int[])ints.Clone());
        }

        public long[]? getLongArray(string key)
        {
            return getLongArray(key, EMPTY_LONG_ARRAY);
        }

        public long[]? getLongArray(string key, long[]? defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is long[] longs)
                return (long[])longs.Clone();

            return defaultValue;
        }

        public void listenForLongArray(string key, Action<long[]> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is long[] longs)
                consumer.Invoke((long[])longs.Clone());
        }

        public NbtMap? getCompound(string key)
        {
            return getCompound(key, EMPTY);
        }

        public NbtMap? getCompound(string key, NbtMap? defaultValue)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is NbtMap nm)
                return nm;

            return defaultValue;
        }

        public void listenForCompound(string key, Action<NbtMap> consumer)
        {
            object? tag = map.GetOrDefault(key);
            if (tag is NbtMap nm)
                consumer.Invoke(nm);
        }

        //    public <T> List<T> getList(string key, NbtType<T> type)
        //    {
        //        return this.getList(key, type, Collections.emptyList());
        //    }

        //    @SuppressWarnings("unchecked")
        //public <T> List<T> getList(string key, NbtType<T> type, @Nullable List<T> defaultValue)
        //    {
        //        object? tag = map.GetOrDefault(key);
        //        if (tag is NbtList && ((NbtList <?>) tag).getType() == type) {
        //            return (NbtList<T>)tag;
        //        }
        //        return defaultValue;
        //    }

        //    @SuppressWarnings("unchecked")
        //public <T> void listenForList(string key, NbtType<T> type, Consumer<List<T>> consumer)
        //    {
        //        object? tag = map.GetOrDefault(key);
        //        if (tag is NbtList<?> && ((NbtList <?>) tag).getType() == type) {
        //            consumer.accept((NbtList<T>)tag);
        //        }
        //    }

        //    public Number getNumber(string key)
        //    {
        //        return getNumber(key, 0);
        //    }

        //    public Number getNumber(string key, Number defaultValue)
        //    {
        //        object? tag = map.GetOrDefault(key);
        //        if (tag is Number) {
        //            return (Number)tag;
        //        }
        //        return defaultValue;
        //    }

        //    public void listenForNumber(string key, NumberConsumer consumer)
        //    {
        //        object? tag = map.GetOrDefault(key);
        //        if (tag is Number) {
        //            consumer.accept((Number)tag);
        //        }
        //    }

        public override bool Equals(object? o)
        {
            if (o == this)
                return true;

            if (o is not NbtMap m)
                return false;
            if (m.Count != Count)
                return false;

            if (hashCodeGenerated && m.hashCodeGenerated && hashCode != ((NbtMap)o).hashCode)
                return false;

            try
            {
                foreach (var e in entrySet())
                {
                    string key = e.Key;
                    object value = e.Value;
                    if (value == null)
                    {
                        if (!(m.get(key) == null && m.containsKey(key)))
                            return false;
                    }
                    else
                    {
                        if (!objects.deepEquals(value, m.get(key)))
                            return false;
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            if (hashCodeGenerated)
                return hashCode;

            int h = 0;
            foreach (var stringobjectEntry in map)
                h += stringobjectEntry.GetHashCode();

            hashCode = h;
            hashCodeGenerated = true;
            return h;
        }

        public override string ToString()
        {
            return mapToString(map);
        }

        internal static string mapToString(IDictionary<string, object> map)
        {
            if (map.Count == 0)
                return "{}";

            StringBuilder sb = new StringBuilder();
            sb.Append('{').Append('\n');

            IEnumerator<KeyValuePair<string, object>> enumerator = map.GetEnumerator();
            enumerator.MoveNext();
            for (; ; )
            {
                var e = enumerator.Current;
                string key = e.Key;
                string value = NbtUtils.tostring(e.Value);

                string str = NbtUtils.indent("\"" + key + "\": " + value);
                sb.Append(str);
                if (!enumerator.MoveNext())
                    return sb.Append('\n').Append('}').ToString();
                sb.Append(',').Append('\n');
            }
        }
    }
}
