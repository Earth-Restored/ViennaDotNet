using System.Text.Json.Serialization;

namespace Solace.TappablesGenerator;

public sealed record Encounter(
    string Id,
    float Lat,
    float Lon,
    long SpawnTime,
    long ValidFor,
    string Icon,
    Encounter.RarityE Rarity,
    string EncounterBuildplateId
)
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RarityE
    {
        COMMON,
        UNCOMMON,
        RARE,
        EPIC,
        LEGENDARY
    }
}