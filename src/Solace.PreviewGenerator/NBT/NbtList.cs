using System.Collections;
using System.Text.Json.Serialization;

namespace Solace.PreviewGenerator.NBT;

public class NbtList : IList
{
    public static readonly NbtList EMPTY = new NbtList(NbtType.END);

    [JsonInclude, JsonPropertyName("type")]
    public readonly NbtType _type;
    [JsonInclude, JsonPropertyName("array")]
    public readonly Array _array;
#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable CS0169
    [JsonIgnore]
    private bool hashCodeGenerated;
    [JsonIgnore]
    private int hashCode;
#pragma warning restore CS0169
#pragma warning restore IDE0044 // Add readonly modifier

    public bool IsFixedSize => true;

    public bool IsReadOnly => true;

    public int Count => _array.Length;

    public bool IsSynchronized => false;

    public object SyncRoot => null!;

    public object? this[int index] { get => Get(index); set => throw new InvalidOperationException(); }

    public NbtList(NbtType type, ICollection collection)
    {
        ArgumentNullException.ThrowIfNull(type, nameof(type));
        _type = type;
        _array = Array.CreateInstance(type.TagType, collection.Count);
        collection.CopyTo(_array, 0);
    }

    public NbtList(NbtType tagClass, params object[] array)
    {
        ArgumentNullException.ThrowIfNull(_type, nameof(tagClass));
        _type = tagClass;
        _array = (Array)array.Clone();
    }

    public new NbtType GetType()
        => _type;

    public object Get(int index)
    {
        if (index < 0 || index >= _array.Length)
            throw new IndexOutOfRangeException("Expected 0-" + (_array.Length - 1) + ". Got " + index);

        return NbtUtils.Copy(_array.GetValue(index)!);
    }

    public int Size()
        => _array.Length;

    public int Add(object? value)
        => throw new InvalidOperationException();

    public void Clear()
        => throw new InvalidOperationException();

    public bool Contains(object? value)
        => Array.IndexOf(_array, value) >= 0;

    public int IndexOf(object? value)
        => Array.IndexOf(_array, value);

    public void Insert(int index, object? value)
        => throw new InvalidOperationException();

    public void Remove(object? value)
        => throw new InvalidOperationException();

    public void RemoveAt(int index)
        => throw new InvalidOperationException();

    public void CopyTo(Array array, int index)
        => _array.CopyTo(array, index);

    public IEnumerator GetEnumerator()
        => _array.GetEnumerator();
}
