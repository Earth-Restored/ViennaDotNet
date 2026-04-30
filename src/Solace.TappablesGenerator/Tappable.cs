using static ViennaDotNet.TappablesGenerator.Tappable;

namespace ViennaDotNet.TappablesGenerator;

public record Tappable(
    string Id,
    float Lat,
    float Lon,
    long SpawnTime,
    long ValidFor,
    string Icon,
    RarityE Rarity,
    Item[] Items
)
{
    public enum RarityE
    {
        COMMON,
        UNCOMMON,
        RARE,
        EPIC,
        LEGENDARY
    }

    public sealed record Item(
        string Id,
        int Count
    );
}
