using System.Text.Json.Serialization;

namespace ViennaDotNet.ApiServer.Types.Buildplates;

public sealed record SharedBuildplate(
    string PlayerId,
    string SharedOn,
    SharedBuildplate.BuildplateDataR BuildplateData,
    Inventory.Inventory inventory
)
{
    public sealed record BuildplateDataR(
        Dimension Dimension,
        Offset Offset,
        int BlocksPerMeter,
        BuildplateDataR.TypeE Type,
        SurfaceOrientation SurfaceOrientation,
        string Model,
        int Order
    )
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum TypeE
        {
            [JsonStringEnumMemberName("Survival")] SURVIVAL,
        }
    }
}