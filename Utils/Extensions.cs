using Newtonsoft.Json;

namespace ViennaDotNet.Utils
{
    public static class Extensions
    {
        public static async Task<T?> AsJson<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
                return JsonConvert.DeserializeObject<T>(await reader.ReadToEndAsync());
        }

        public static void AddRange<TKey, TValue>(this IDictionary<TKey, TValue> dic, IDictionary<TKey, TValue> dicToAdd)
        {
            dicToAdd.ForEach(x => dic.Add(x.Key, x.Value));
        }

        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (var item in source)
                action(item);
        }
    }
}
