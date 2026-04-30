using System.Collections;
using System.Reflection;

namespace ViennaDotNet.Common.Utils;

public static class ObjectExtensions
{
    public static bool DeepEquals(object? obj1, object? obj2)
    {
        if (ReferenceEquals(obj1, obj2))
        {
            return true;
        }

        if (obj1 is null || obj2 is null)
        {
            return false;
        }

        Type type1 = obj1.GetType();
        Type type2 = obj2.GetType();

        if (type1 != type2)
        {
            return false;
        }

        if (type1.IsPrimitive || obj1 is string)
        {
            return obj1.Equals(obj2);
        }

        if (obj1 is IEnumerable enumerable1 && obj2 is IEnumerable enumerable2)
        {
            IEnumerator enumerator1 = enumerable1.GetEnumerator();
            IEnumerator enumerator2 = enumerable2.GetEnumerator();

            while (enumerator1.MoveNext() && enumerator2.MoveNext())
            {
                if (!DeepEquals(enumerator1.Current, enumerator2.Current))
                {
                    return false;
                }
            }

            return !(enumerator1.MoveNext() || enumerator2.MoveNext());
        }

        foreach (PropertyInfo property in type1.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value1 = property.GetValue(obj1);
            object? value2 = property.GetValue(obj2);

            if (!DeepEquals(value1, value2))
            {
                return false;
            }
        }

        return true;
    }
}
