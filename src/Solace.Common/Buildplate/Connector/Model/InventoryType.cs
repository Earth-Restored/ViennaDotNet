using System.Text.Json.Serialization;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Solace.Buildplate.Connector.Model;
#pragma warning restore IDE0130 // Namespace does not match folder structure

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InventoryType
{
    SYNCED,
    DISCARD,
    BACKPACK
}