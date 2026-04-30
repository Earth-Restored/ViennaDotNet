using Solace.Common.Utils;
using Solace.DB;

namespace Solace.ApiServer.Utils;

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
            set(updates, "profile", "characterProfile");
            set(updates, "inventory", "inventory");
            set(updates, "crafting", "crafting");
            set(updates, "smelting", "smelting");
            set(updates, "boosts", "boosts");
            set(updates, "buildplates", "buildplates");
            set(updates, "journal", "playerJournal");
            set(updates, "challenges", "challenges");
            set(updates, "tokens", "tokens");
        }

        private void set(Dictionary<string, int?> updates, string name, string @as)
        {
            int? version = updates.GetOrDefault(name, null);
            if (version is not null)
            {
                Map[@as] = version;
            }
        }
    }
}