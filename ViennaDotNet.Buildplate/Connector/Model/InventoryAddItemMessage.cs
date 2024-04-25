using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Buildplate.Connector.Model
{
    public record InventoryAddItemMessage(
         string playerId,
         string itemId,
         int count,
         string? instanceId,
         int wear
    )
    {
    }
}
