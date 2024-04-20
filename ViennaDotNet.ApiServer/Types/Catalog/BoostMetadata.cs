using Newtonsoft.Json.Linq;
using static ViennaDotNet.ApiServer.Types.Catalog.BoostMetadata;
using System.Reflection.Emit;
using System.Xml.Linq;
using System;

namespace ViennaDotNet.ApiServer.Types.Catalog
{
    public record BoostMetadata(
        string name,
        string type,
        string attribute,
        bool canBeDeactivated,
        bool canBeRemoved,
        string? activeDuration,
        bool additive,
        int? level,
        Effect[] effects,
        string? scenario,
        string? cooldown
    )
    {
        public record Effect(
            string type,
            string? duration,
            double? value,
            string unit,
            string targets,
            string[] items,
            string[] itemScenarios,
            string activation,
            string? modifiesType
        )
        {
        }
    }
}
