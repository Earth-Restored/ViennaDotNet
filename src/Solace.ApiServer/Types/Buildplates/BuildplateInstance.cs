using System.Text.Json.Serialization;
using ViennaDotNet.ApiServer.Types.Common;
using static ViennaDotNet.ApiServer.Types.Buildplates.BuildplateInstance;

namespace ViennaDotNet.ApiServer.Types.Buildplates;

public sealed record BuildplateInstance(
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
    public enum ApplicationStatusE
    {
        [JsonStringEnumMemberName("Unknown")] UNKNOWN,
        [JsonStringEnumMemberName("Ready")] READY
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ServerStatusE
    {
        [JsonStringEnumMemberName("Running")] RUNNING
    }

    public sealed record GameplayMetadataR(
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
        public enum GameplayModeE
        {
            [JsonStringEnumMemberName("Buildplate")] BUILDPLATE,
            [JsonStringEnumMemberName("BuildplatePlay")] BUILDPLATE_PLAY,
            [JsonStringEnumMemberName("SharedBuildplatePlay")] SHARED_BUILDPLATE_PLAY,
            [JsonStringEnumMemberName("Encounter")] ENCOUNTER
        }
    }
}
