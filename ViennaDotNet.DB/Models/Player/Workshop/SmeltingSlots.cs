using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.DB.Models.Player.Workshop
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SmeltingSlots
    {
        [JsonProperty]
        public readonly SmeltingSlot[] slots;

        public SmeltingSlots()
        {
            slots = new SmeltingSlot[] { new SmeltingSlot(), new SmeltingSlot(), new SmeltingSlot() };
        }
    }
}
