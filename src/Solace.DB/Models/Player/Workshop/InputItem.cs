using ViennaDotNet.DB.Models.Common;

namespace ViennaDotNet.DB.Models.Player.Workshop;

public sealed record InputItem(
     string Id,
     int Count,
     NonStackableItemInstance[] Instances
);
