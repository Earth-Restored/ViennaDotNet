using System.Text.Json.Serialization;
using Solace.ApiServer.Types.Common;

namespace Solace.ApiServer.Types.Tappables;

public record ActiveLocation(
    string Id,
    string TileId,
    Coordinate Coordinate,
    string SpawnTime,
    string ExpirationTime,
    ActiveLocation.TypeE Type,
    string Icon,
    ActiveLocation.MetadataR Metadata,
    ActiveLocation.TappableMetadataR? TappableMetadata,
    ActiveLocation.EncounterMetadataR? EncounterMetadata
)
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TypeE
    {
#pragma warning disable CA1707 // Identifiers should not contain underscores
        [JsonStringEnumMemberName("Tappable")] TAPPABLE,
        [JsonStringEnumMemberName("Encounter")] ENCOUNTER,
        [JsonStringEnumMemberName("PlayerAdventure")] PLAYER_ADVENTURE,
#pragma warning restore CA1707 // Identifiers should not contain underscores
    }

    public sealed record MetadataR(
        string RewardId,
        Rarity Rarity
    );

    public sealed record TappableMetadataR(
        Rarity Rarity
    );

    public sealed record EncounterMetadataR(
        EncounterMetadataR.EncounterTypeE EncounterType,
        string LocationId,
        string WorldId,
        EncounterMetadataR.AnchorStateE AnchorState,
        string AnchorId,
        string AugmentedImageSetId
    )
    {
        // TODO: what do these actually do?
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum EncounterTypeE
        {
            [JsonStringEnumMemberName("None")] NONE,
#pragma warning disable CA1707 // Identifiers should not contain underscores
            [JsonStringEnumMemberName("Short4X4Peaceful")] SHORT_4X4_PEACEFUL,
            [JsonStringEnumMemberName("Short4X4Hostile")] SHORT_4X4_HOSTILE,
            [JsonStringEnumMemberName("Short8X8Peaceful")] SHORT_8X8_PEACEFUL,
            [JsonStringEnumMemberName("Short8X8Hostile")] SHORT_8X8_HOSTILE,
            [JsonStringEnumMemberName("Short16X16Peaceful")] SHORT_16X16_PEACEFUL,
            [JsonStringEnumMemberName("Short16X16Hostile")] SHORT_16X16_HOSTILE,
            [JsonStringEnumMemberName("Tall4X4Peaceful")] TALL_4X4_PEACEFUL,
            [JsonStringEnumMemberName("Tall4X4Hostile")] TALL_4X4_HOSTILE,
            [JsonStringEnumMemberName("Tall8X8Peaceful")] TALL_8X8_PEACEFUL,
            [JsonStringEnumMemberName("Tall8X8Hostile")] TALL_8X8_HOSTILE,
            [JsonStringEnumMemberName("Tall16X16Peaceful")] TALL_16X16_PEACEFUL,
            [JsonStringEnumMemberName("Tall16X16Hostile")] TALL_16X16_HOSTILE,
#pragma warning restore CA1707 // Identifiers should not contain underscores
        }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum AnchorStateE
        {
            [JsonStringEnumMemberName("Off")] OFF,
        }
    }
}
