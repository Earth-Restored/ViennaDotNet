using System.Text.Json.Serialization;
using Solace.ApiServer.Types.Common;
using static Solace.ApiServer.Types.Buildplates.BuildplateInstance;

namespace Solace.ApiServer.Types.Buildplates;

internal sealed record BuildplateInstance(
    string InstanceId,
    string PartitionId,
    string Fqdn,
    string IpV4Address,
    int Port,
    bool ServerReady,
    ApplicationStatusE ApplicationStatus,
    ServerStatusE ServerStatus,
    string Metadata,
    GameplayMetadataR GameplayMetadata,
    string RoleInstance,    // TODO: find out what this is
    Coordinate HostCoordinate
)
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal enum ApplicationStatusE
    {
        [JsonStringEnumMemberName("Unknown")] UNKNOWN,
        [JsonStringEnumMemberName("Ready")] READY
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal enum ServerStatusE
    {
        [JsonStringEnumMemberName("Running")] RUNNING
    }

    internal sealed record GameplayMetadataR(
        string WorldId,
        string TemplateId,
        string? SpawningPlayerId,
        string SpawningClientBuildNumber,
        string PlayerJoinCode,
        Dimension Dimension,
        Offset Offset,
        int BlocksPerMeter,
        bool IsFullSize,
        GameplayMetadataR.GameplayModeE GameplayMode,
        SurfaceOrientation SurfaceOrientation,
        string? AugmentedImageSetId,
        Rarity? Rarity,
        Dictionary<string, object> BreakableItemToItemLootMap    // TODO: find out what this is
    )
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        internal enum GameplayModeE
        {
#pragma warning disable CA1707 // Identifiers should not contain underscores
            [JsonStringEnumMemberName("Buildplate")] BUILDPLATE,
            [JsonStringEnumMemberName("BuildplatePlay")] BUILDPLATE_PLAY,
            [JsonStringEnumMemberName("SharedBuildplatePlay")] SHARED_BUILDPLATE_PLAY,
            [JsonStringEnumMemberName("Encounter")] ENCOUNTER
#pragma warning restore CA1707 // Identifiers should not contain underscores
        }
    }
}
