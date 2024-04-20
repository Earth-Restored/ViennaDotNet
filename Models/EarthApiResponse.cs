using ViennaDotNet.Utils;

namespace ViennaDotNet.Models
{
    public class EarthApiResponse
    {
        public object result;
        public Dictionary<string, int> updates = new Dictionary<string, int>();

        public EarthApiResponse(object _results)
        {
            result = _results;
        }

        public EarthApiResponse(object _results, Updates _updates)
        {
            result = _results;
            updates.AddRange(_updates.map);
        }

        public class Updates
        {
            public Dictionary<string, int> map = new Dictionary<string, int>();
        }
    }
}
