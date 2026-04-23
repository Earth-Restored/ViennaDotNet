using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;

namespace ViennaDotNet.ApiServer.Utils;

public class EarthApiResponse
{
    public object? Result { get; }
    public Dictionary<string, int?>? Updates { get; } = [];

    public EarthApiResponse(object results)
    {
        Result = results;
    }

    public EarthApiResponse(object? results, UpdatesResponse? updates)
    {
        Result = results;
        if (updates is null)
        {
            Updates = null;
        }
        else
        {
            Updates.AddRange(updates.Map);
        }
    }

    public sealed class UpdatesResponse
    {
        public Dictionary<string, int?> Map = [];

        public UpdatesResponse(EarthDB.Results results)
        {
            Dictionary<string, int?> updates = results.GetUpdates();
            Set(updates, "profile", "characterProfile");
            Set(updates, "inventory", "inventory");
            Set(updates, "crafting", "crafting");
            Set(updates, "smelting", "smelting");
            Set(updates, "boosts", "boosts");
            Set(updates, "buildplates", "buildplates");
            Set(updates, "journal", "playerJournal");
            Set(updates, "challenges", "challenges");
            Set(updates, "tokens", "tokens");
        }

        private void Set(Dictionary<string, int?> updates, string name, string @as)
        {
            int? version = updates.GetOrDefault(name, null);
            if (version is not null)
            {
                Map[@as] = version;
            }
        }
    }
}