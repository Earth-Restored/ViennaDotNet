using Newtonsoft.Json;

namespace ViennaDotNet.DB.Models.Player.Workshop;

[JsonObject(MemberSerialization.OptIn)]
public sealed class CraftingSlot
{
    [JsonProperty]
    public ActiveJob? activeJob;
    [JsonProperty]
    public bool locked;

    public CraftingSlot()
    {
        activeJob = null;
        locked = false;
    }

    public record ActiveJob(
        string sessionId,
        string recipeId,
        long startTime,
        InputItem[][] input,
        int totalRounds,
        int collectedRounds,
        bool finishedEarly
    )
    {
    }
}
