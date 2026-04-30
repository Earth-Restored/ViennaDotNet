using System.Text.Json.Serialization;

namespace Solace.Buildplate.Connector.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InventoryType
{
    SYNCED,
    DISCARD,
    BACKPACK
}