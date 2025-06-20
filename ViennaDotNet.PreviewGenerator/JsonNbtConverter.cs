using System.Text.Json.Serialization;
using ViennaDotNet.Common.Excceptions;
using ViennaDotNet.PreviewGenerator.NBT;

namespace ViennaDotNet.PreviewGenerator;

internal sealed class JsonNbtConverter
{
    public static JsonNbtTag convert(NbtMap tag)
    {
        Dictionary<string, JsonNbtTag> value = [];
        foreach (var entry in tag.entrySet())
            value[entry.Key] = convert(entry.Value);

        return new CompoundJsonNbtTag(value);
    }

    public static JsonNbtTag convert(NbtList tag)
    {
        LinkedList<JsonNbtTag> value = new();
        foreach (object item in tag)
            value.AddLast(convert(item));

        return new ListJsonNbtTag([.. value]);
    }

    private static JsonNbtTag convert(object tag)
    {
        if (tag is NbtMap map)
            return convert(map);
        else if (tag is NbtList list)
            return convert(list);
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
        public enum Type
        {
            Compound,
            List,
            Int,
            Byte,
            Float,
            String
        }

        public readonly Type type;
        public readonly object value;

        public JsonNbtTag(Type type, object value)
        {
            this.type = type;
            this.value = value;
        }
    }

    public sealed class CompoundJsonNbtTag : JsonNbtTag
    {
        public CompoundJsonNbtTag(Dictionary<string, JsonNbtTag> value)
            : base(Type.Compound, value)
        {
        }
    }

    public sealed class ListJsonNbtTag : JsonNbtTag
    {
        public ListJsonNbtTag(JsonNbtTag[] value)
            : base(Type.List, value)
        {
        }
    }

    public sealed class IntJsonNbtTag : JsonNbtTag
    {
        public IntJsonNbtTag(int value)
            : base(Type.Int, value)
        {
        }
    }

    public sealed class ByteJsonNbtTag : JsonNbtTag
    {
        public ByteJsonNbtTag(byte value)
            : base(Type.Byte, value)
        {
        }
    }

    public sealed class FloatJsonNbtTag : JsonNbtTag
    {
        public FloatJsonNbtTag(float value)
            : base(Type.Float, value)
        {
        }
    }

    public sealed class StringJsonNbtTag : JsonNbtTag
    {
        public StringJsonNbtTag(string value)
            : base(Type.String, value)
        {
        }
    }
}
