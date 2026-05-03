using Solace.Common.Utils;

namespace Solace.PreviewGenerator.NBT;

public sealed class NbtType
{
#pragma warning disable CA1720 // Identifier contains type name
    public static readonly NbtType End = new NbtType(typeof(void), EnumE.End);
    public static readonly NbtType Byte = new NbtType(typeof(byte), EnumE.Byte);
    public static readonly NbtType Short = new NbtType(typeof(short), EnumE.Short);
    public static readonly NbtType Int = new NbtType(typeof(int), EnumE.Int);
    public static readonly NbtType Long = new NbtType(typeof(long), EnumE.Long);
    public static readonly NbtType Float = new NbtType(typeof(float), EnumE.Float);
    public static readonly NbtType Double = new NbtType(typeof(double), EnumE.Double);
    public static readonly NbtType ByteArray = new NbtType(typeof(byte[]), EnumE.ByteArray);
    public static readonly NbtType String = new NbtType(typeof(string), EnumE.String);

    public static readonly NbtType List = new NbtType(typeof(NbtList), EnumE.List);
    public static readonly NbtType Compound = new NbtType(typeof(NbtMap), EnumE.Compound);
    public static readonly NbtType IntArray = new NbtType(typeof(int[]), EnumE.IntArray);
    public static readonly NbtType LongArray = new NbtType(typeof(long[]), EnumE.LongArray);
#pragma warning restore CA1720 // Identifier contains type name

    private static readonly NbtType[] BY_ID = [End, Byte, Short, Int, Long, Float, Double, ByteArray, String, List, Compound, IntArray, LongArray];

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
            throw new ArgumentOutOfRangeException(nameof(id), "Tag type id must be greater than 0 and less than " + (BY_ID.Length - 1));
        }
    }

    public static NbtType ByType(Type tagClass)
    {
        NbtType? type = BY_CLASS.GetOrDefault(tagClass);
        return type is null ? throw new ArgumentException("Tag of class " + tagClass + " does not exist", nameof(tagClass)) : type;
    }

    public enum EnumE : int
    {
#pragma warning disable CA1720 // Identifier contains type name
        End,
        Byte,
        Short,
        Int,
        Long,
        Float,
        Double,
        ByteArray,
        String,
        List,
        Compound,
        IntArray,
        LongArray
#pragma warning restore CA1720 // Identifier contains type name
    }
}

public static class NbtTypeEnumExtensions
{
    public static string GetName(this NbtType.EnumE e)
        => "TAG_" + Enum.GetName(e);
}
