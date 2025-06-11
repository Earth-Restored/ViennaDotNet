using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;

namespace ViennaDotNet.ApiServer.Utils;

public class EarthApiResponse
{
    public object? result;
    public Dictionary<string, int?>? updates = [];

    public EarthApiResponse(object _results)
    {
        result = _results;
    }

    public EarthApiResponse(object? _results, Updates? _updates)
    {
        result = _results;
        if (_updates is null)
            updates = null;
        else
            updates.AddRange(_updates.map);
    }

    public sealed class Updates
    {
        public Dictionary<string, int?> map = [];

        public Updates(EarthDB.Results results)
        {
            Dictionary<string, int?> updates = results.getUpdates();
            put(updates, "profile", "characterProfile");
            put(updates, "inventory", "inventory");
            put(updates, "crafting", "crafting");
            put(updates, "smelting", "smelting");
            put(updates, "boosts", "boosts");
            put(updates, "buildplates", "buildplates");
            put(updates, "journal", "playerJournal");
            put(updates, "challenges", "challenges");
            put(updates, "tokens", "tokens");
        }

        private void put(Dictionary<string, int?> updates, string name, string @as)
        {
            int? version = updates.GetOrDefault(name, null);
            if (version != null)
                map[@as] = version;
        }
    }
}

//public class EarthApiResponsePlus
//{
//    public object result;
//    public object? expiration;
//    public object? continuationToken;
//    public Dictionary<string, int>? updates = new Dictionary<string, int>();

//    public EarthApiResponsePlus(object _results)
//    {
//        result = _results;
//    }

//    public EarthApiResponsePlus(object _results, Updates? _updates)
//    {
//        result = _results;
//        if (_updates is null)
//            updates = null;
//        else
//            updates.AddRange(_updates.map);
//    }

//    public class Updates
//    {
//        public Dictionary<string, int> map = new Dictionary<string, int>();
//    }
//}
