using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.PreviewGenerator.NBT;

public sealed class NbtType
{
    public static readonly NbtType END = new NbtType(typeof(void), EnumE.END);
    public static readonly NbtType BYTE = new NbtType(typeof(byte), EnumE.BYTE);
    public static readonly NbtType SHORT = new NbtType(typeof(short), EnumE.SHORT);
    public static readonly NbtType INT = new NbtType(typeof(int), EnumE.INT);
    public static readonly NbtType LONG = new NbtType(typeof(long), EnumE.LONG);
    public static readonly NbtType FLOAT = new NbtType(typeof(float), EnumE.FLOAT);
    public static readonly NbtType DOUBLE = new NbtType(typeof(double), EnumE.DOUBLE);
    public static readonly NbtType BYTE_ARRAY = new NbtType(typeof(byte[]), EnumE.BYTE_ARRAY);
    public static readonly NbtType STRING = new NbtType(typeof(string), EnumE.STRING);

    public static readonly NbtType LIST = new NbtType(typeof(NbtList), EnumE.LIST);
    public static readonly NbtType COMPOUND = new NbtType(typeof(NbtMap), EnumE.COMPOUND);
    public static readonly NbtType INT_ARRAY = new NbtType(typeof(int[]), EnumE.INT_ARRAY);
    public static readonly NbtType LONG_ARRAY = new NbtType(typeof(long[]), EnumE.LONG_ARRAY);

    private static readonly NbtType[] BY_ID = [END, BYTE, SHORT, INT, LONG, FLOAT, DOUBLE, BYTE_ARRAY, STRING, LIST, COMPOUND, INT_ARRAY, LONG_ARRAY];

    private static readonly Dictionary<Type, NbtType> BY_CLASS = [];

    static NbtType()
    {
        foreach (NbtType type in BY_ID)
        {
            BY_CLASS.Add(type.TagType, type);
        }
    }

    private readonly Type _tagType;
    private readonly EnumE _enumeration;

    private NbtType(Type tagType, EnumE enumeration)
    {
        _tagType = tagType;
        _enumeration = enumeration;
    }

    public Type TagType => _tagType;

    public int Id => (int)_enumeration;

    public string TypeName => _enumeration.GetName();

    public EnumE Enum => _enumeration;

    public static NbtType ById(int id)
    {
        if (id >= 0 && id < BY_ID.Length)
        {
            return BY_ID[id];
        }
        else
        {
            throw new IndexOutOfRangeException("Tag type id must be greater than 0 and less than " + (BY_ID.Length - 1));
        }
    }

    public static NbtType ByType(Type tagClass)
    {
        NbtType? type = BY_CLASS.GetOrDefault(tagClass);
        return type is null ? throw new ArgumentException("Tag of class " + tagClass + " does not exist", nameof(tagClass)) : type;
    }

    public enum EnumE : int
    {
        END,
        BYTE,
        SHORT,
        INT,
        LONG,
        FLOAT,
        DOUBLE,
        BYTE_ARRAY,
        STRING,
        LIST,
        COMPOUND,
        INT_ARRAY,
        LONG_ARRAY
    }
}

public static class NbtType_EnumExtensions
{
    public static string GetName(this NbtType.EnumE e)
        => "TAG_" + Enum.GetName(e);
}
