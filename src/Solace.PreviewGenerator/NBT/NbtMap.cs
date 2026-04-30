using System.Text;
using System.Text.Json.Serialization;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.PreviewGenerator.NBT;

public class NbtMap// : IDictionary<string, object>
{
    public static readonly NbtMap EMPTY = new NbtMap();

    private static readonly byte[] EMPTY_BYTE_ARRAY = [];
    private static readonly int[] EMPTY_INT_ARRAY = [];
    private static readonly long[] EMPTY_LONG_ARRAY = [];

    [JsonInclude, JsonPropertyName("map")]
    public readonly IDictionary<string, object> _map;

    public int Count => _map.Count;

    [JsonIgnore]
    private bool hashCodeGenerated;
    [JsonIgnore]
    private int hashCode;

    private NbtMap()
    {
        _map = new Dictionary<string, object>();
    }

    internal NbtMap(IDictionary<string, object> map)
    {
        _map = map;
    }

    public static NbtMapBuilder builder() => [];

    public static NbtMap fromMap(IDictionary<string, object> map) => new NbtMap(map.AsReadOnly());

    public NbtMapBuilder toBuilder() => NbtMapBuilder.From(this);

    public bool ContainsKey(string key)
        => _map.ContainsKey(key);

    public bool ContainsKey(string key, NbtType type)
    {
        if (_map.TryGetValue(key, out object? o))
            return o.GetType() == type.TagType;
        else
            return false;
    }

    public object Get(string key)
        => NbtUtils.Copy(_map.GetOrDefault(key));

    public ICollection<string> KeySet()
        => _map.Keys;

    public ICollection<KeyValuePair<string, object>> EntrySet()
        => _map;

    public ICollection<object> Values()
        => _map.Values;

    public bool Getbool(string key)
        => Getbool(key, false);

    public bool Getbool(string key, bool defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is byte b)
            return b != 0;

        return defaultValue;
    }

    public byte GetByte(string key) => GetByte(key, 0);

    public byte GetByte(string key, byte defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is byte b)
            return b;

        return defaultValue;
    }

    public short GetShort(string key) => GetShort(key, 0);

    public short GetShort(string key, short defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is short s)
            return s;

        return defaultValue;
    }

    public int GetInt(string key) => GetInt(key, 0);

    public int GetInt(string key, int defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is int i)
            return i;

        return defaultValue;
    }

    public long GetLong(string key) => GetLong(key, 0L);

    public long GetLong(string key, long defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is long l)
            return l;

        return defaultValue;
    }

    public float GetFloat(string key) => GetFloat(key, 0F);

    public float GetFloat(string key, float defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is float f)
            return f;

        return defaultValue;
    }

    public double GetDouble(string key) => GetDouble(key, 0.0);

    public double GetDouble(string key, double defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is double d)
            return d;

        return defaultValue;
    }

    public string? GetString(string key) => Getstring(key, "");

    public string? Getstring(string key, string? defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is string s)
            return s;

        return defaultValue;
    }

    public byte[]? GetByteArray(string key) => GetByteArray(key, EMPTY_BYTE_ARRAY);

    public byte[]? GetByteArray(string key, byte[]? defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is byte[] bytes)
            return (byte[])bytes.Clone();

        return defaultValue;
    }

    public int[]? GetIntArray(string key) => GetIntArray(key, EMPTY_INT_ARRAY);

    public int[]? GetIntArray(string key, int[]? defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is int[] ints)
            return (int[])ints.Clone();

        return defaultValue;
    }

    public long[]? GetLongArray(string key) => GetLongArray(key, EMPTY_LONG_ARRAY);

    public long[]? GetLongArray(string key, long[]? defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is long[] longs)
            return (long[])longs.Clone();

        return defaultValue;
    }

    public NbtMap? GetCompound(string key) => GetCompound(key, EMPTY);

    public NbtMap? GetCompound(string key, NbtMap? defaultValue)
    {
        object? tag = _map.GetOrDefault(key);
        if (tag is NbtMap nm)
            return nm;

        return defaultValue;
    }

    //    public <T> List<T> GetList(string key, NbtType<T> type)
    //    {
    //        return this.getList(key, type, Collections.emptyList());
    //    }

    //    @SuppressWarnings("unchecked")
    //public <T> List<T> GetList(string key, NbtType<T> type, @Nullable List<T> defaultValue)
    //    {
    //        object? tag = map.GetOrDefault(key);
    //        if (tag is NbtList && ((NbtList <?>) tag).getType() == type) {
    //            return (NbtList<T>)tag;
    //        }
    //        return defaultValue;
    //    }

    //    public Number GetNumber(string key)
    //    {
    //        return getNumber(key, 0);
    //    }

    //    public Number GetNumber(string key, Number defaultValue)
    //    {
    //        object? tag = map.GetOrDefault(key);
    //        if (tag is Number) {
    //            return (Number)tag;
    //        }
    //        return defaultValue;
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
            foreach (var e in EntrySet())
            {
                string key = e.Key;
                object value = e.Value;
                if (value is null)
                {
                    if (!(m.Get(key) is null && m.ContainsKey(key)))
                        return false;
                }
                else
                {
                    if (!ObjectExtensions.DeepEquals(value, m.Get(key)))
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
        foreach (var stringobjectEntry in _map)
            h += stringobjectEntry.GetHashCode();

        hashCode = h;
        hashCodeGenerated = true;
        return h;
    }

    public override string ToString()
        => MapToString(_map);

    internal static string MapToString(IDictionary<string, object> map)
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
            string value = NbtUtils.ToString(e.Value);

            string str = NbtUtils.Indent("\"" + key + "\": " + value);
            sb.Append(str);
            if (!enumerator.MoveNext())
                return sb.Append('\n').Append('}').ToString();
            sb.Append(',').Append('\n');
        }
    }
}
