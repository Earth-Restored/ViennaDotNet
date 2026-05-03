using System.Text.Json.Serialization;

namespace Solace.ApiServer.Types.Buildplates;

internal sealed record SharedBuildplate(
    string PlayerId,
    string SharedOn,
    SharedBuildplate.BuildplateDataR BuildplateData,
    Inventory.Inventory Inventory
)
{
    internal sealed record BuildplateDataR(
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
        internal enum TypeE
        {
            [JsonStringEnumMemberName("Survival")] SURVIVAL,
        }
    }
}