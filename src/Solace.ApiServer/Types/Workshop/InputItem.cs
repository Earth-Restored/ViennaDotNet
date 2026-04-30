namespace ViennaDotNet.ApiServer.Types.Workshop;

public sealed record InputItem(
     string ItemId,
     int Quantity,
     string[] InstanceIds
);