using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.DB.Models.Player.Workshop
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CraftingSlots
    {
        [JsonProperty]
        public readonly CraftingSlot[] slots;

        public CraftingSlots()
        {
            slots = new CraftingSlot[] { new CraftingSlot(), new CraftingSlot(), new CraftingSlot() };
        }
    }
}
