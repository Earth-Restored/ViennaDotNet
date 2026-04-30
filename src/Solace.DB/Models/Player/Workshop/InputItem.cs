using Solace.DB.Models.Common;

namespace Solace.DB.Models.Player.Workshop;

public sealed record InputItem(
     string Id,
     int Count,
     NonStackableItemInstance[] Instances
);
