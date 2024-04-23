using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.DB.Models.Player.Workshop
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SmeltingSlot
    {
        [JsonProperty]
        public ActiveJob? activeJob;
        [JsonProperty]
        public Burning? burning;
        [JsonProperty]
        public bool locked;

        public SmeltingSlot()
        {
            activeJob = null;
            burning = null;
            locked = false;
        }

        public record ActiveJob(
            string sessionId,
            string recipeId,
            long startTime,
            InputItem input,
            Fuel? addedFuel,
            int totalRounds,
            int collectedRounds,
            bool finishedEarly
        )
        {
        }

        public record Fuel(
            InputItem item,
            int burnDuration,
            int heatPerSecond
        )
        {
        }

        public record Burning(
            Fuel fuel,
            int remainingHeat
        )
        {
        }
    }
}
