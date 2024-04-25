using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Buildplate.Connector.Model
{
    public record InventoryUpdateItemWearMessage(
        string playerId,
        string itemId,
        string instanceId,
        int wear
    )
    {
    }
}
