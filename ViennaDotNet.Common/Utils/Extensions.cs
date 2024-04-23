using Newtonsoft.Json;

namespace ViennaDotNet.Common.Utils
{
    public static class Extensions
    {
        public static async Task<T?> AsJson<T>(this Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream))
                return JsonConvert.DeserializeObject<T>(await reader.ReadToEndAsync());
        }

        public static U? Map<T, U>(this T? value, Func<T, U> mapper)
        {
            if (value is null) return default;
            else return mapper(value);
        }

        //public static T? OrElse<T>(this T? value, T other)
        //{
        //    if (value is null)
        //        return other;
        //    else
        //        return value;
        //}
    }
}
