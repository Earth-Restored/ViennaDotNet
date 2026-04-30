using System.Text.Json.Serialization;
using ViennaDotNet.Common.Excceptions;
using ViennaDotNet.PreviewGenerator.NBT;

namespace ViennaDotNet.PreviewGenerator;

internal sealed class JsonNbtConverter
{
    public static JsonNbtTag Convert(NbtMap tag)
    {
        Dictionary<string, JsonNbtTag> value = [];
        foreach (var entry in tag.EntrySet())
            value[entry.Key] = Convert(entry.Value);

        return new CompoundJsonNbtTag(value);
    }

    public static JsonNbtTag Convert(NbtList tag)
    {
        LinkedList<JsonNbtTag> value = new();
        foreach (object item in tag)
            value.AddLast(Convert(item));

        return new ListJsonNbtTag([.. value]);
    }

    private static JsonNbtTag Convert(object tag)
    {
        if (tag is NbtMap map)
            return Convert(map);
        else if (tag is NbtList list)
            return Convert(list);
        else if (tag is int i)
            return new IntJsonNbtTag(i);
        else if (tag is byte b)
            return new ByteJsonNbtTag(b);
        else if (tag is float f)
            return new FloatJsonNbtTag(f);
        else if (tag is string s)
            return new StringJsonNbtTag(s);
        else
            throw new UnsupportedOperationException($"Cannot convert tag of type {tag.GetType().Name}");
    }

    public abstract class JsonNbtTag
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum TypeE
        {
            [JsonStringEnumMemberName("compound")] Compound,
            [JsonStringEnumMemberName("list")] List,
            [JsonStringEnumMemberName("int")] Int,
            [JsonStringEnumMemberName("byte")] Byte,
            [JsonStringEnumMemberName("float")] Float,
            [JsonStringEnumMemberName("string")] String
        }

        public readonly TypeE Type;
        public readonly object Value;

        public JsonNbtTag(TypeE type, object value)
        {
            Type = type;
            Value = value;
        }
    }

    public sealed class CompoundJsonNbtTag : JsonNbtTag
    {
        public CompoundJsonNbtTag(Dictionary<string, JsonNbtTag> value)
            : base(TypeE.Compound, value)
        {
        }
    }

    public sealed class ListJsonNbtTag : JsonNbtTag
    {
        public ListJsonNbtTag(JsonNbtTag[] value)
            : base(TypeE.List, value)
        {
        }
    }

    public sealed class IntJsonNbtTag : JsonNbtTag
    {
        public IntJsonNbtTag(int value)
            : base(TypeE.Int, value)
        {
        }
    }

    public sealed class ByteJsonNbtTag : JsonNbtTag
    {
        public ByteJsonNbtTag(byte value)
            : base(TypeE.Byte, value)
        {
        }
    }

    public sealed class FloatJsonNbtTag : JsonNbtTag
    {
        public FloatJsonNbtTag(float value)
            : base(TypeE.Float, value)
        {
        }
    }

    public sealed class StringJsonNbtTag : JsonNbtTag
    {
        public StringJsonNbtTag(string value)
            : base(TypeE.String, value)
        {
        }
    }
}
