using System.Text.Json.Serialization;

namespace ViennaDotNet.Buildplate.Connector.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InventoryType
{
    SYNCED,
    DISCARD,
    BACKPACK
}