using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Common.Utils
{
    public static class DictionaryExtensions
    {
        public static void AddRange<TKey, TValue>(this IDictionary<TKey, TValue> dic, IDictionary<TKey, TValue> dicToAdd)
        {
            dicToAdd.ForEach(x => dic[x.Key] = x.Value);
        }

        public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key)
        {
            if (dic.TryGetValue(key, out TValue? value)) return value;
            else return default;
        }
        public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key, TValue? defaultValue)
        {
            if (dic.TryGetValue(key, out TValue? value)) return value;
            else return defaultValue;
        }

        public static void ForEach<TKey, TValue>(this IDictionary<TKey, TValue> dic, Action<TKey, TValue> loopAction)
        {
            foreach (var item in dic)
                loopAction(item.Key, item.Value);
        }

        public static TValue? JavaRemove<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key)
        {
            TValue? value;
            if (!dic.TryGetValue(key, out value))
                value = default;

            dic.Remove(key);

            return value;
        }

        public static TValue? ComputeIfAbsent<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key, Func<TKey, TValue?> mappingFunction)
        {
            if (dic.TryGetValue(key, out TValue? value)) return value;
            else
            {
                TValue? newValue = mappingFunction(key);
                if (newValue is null)
                    return default;
                else
                {
                    dic.Add(key, newValue);
                    return newValue;
                }
            }
        }

        public static void RemoveIf<TKey, TValue>(this IDictionary<TKey, TValue> dic, Predicate<KeyValuePair<TKey, TValue>> predicate)
        {
            List<TKey> toRemove = new List<TKey>();

            foreach (var item in dic)
                if (predicate(item))
                    toRemove.Add(item.Key);

            for (int i = 0; i < toRemove.Count; i++)
                dic.Remove(toRemove[i]);
        }
    }
}
