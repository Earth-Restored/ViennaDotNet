using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Buildplate.Connector.Model
{
    public record InventorySetHotbarMessage(
        string playerId,
        InventorySetHotbarMessage.Item[] items
    )
    {
        public record Item(
            string itemId,
            int count,
            string? instanceId
        )
        {
        }
    }
}
