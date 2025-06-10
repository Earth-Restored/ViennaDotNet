using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViennaDotNet.StaticData;

public sealed class EncountersConfig
{
    public readonly EncounterConfig[] encounters;

    internal EncountersConfig(string dir)
    {
        try
        {
            LinkedList<EncounterConfig> encounters = [];
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                if (Path.GetExtension(file) != ".json")
                {
                    continue;
                }

                using (var stream = File.OpenRead(file))
                {
                    var encounter = JsonSerializer.Deserialize<EncounterConfig>(stream);

                    Debug.Assert(encounter is not null);

                    encounters.AddLast(encounter);
                }
            }

            this.encounters = [.. encounters];
        }
        catch (Exception exception)
        {
            throw new StaticDataException(null, exception);
        }
    }

    public record EncounterConfig(
        string icon,
        EncounterConfig.Rarity rarity,
        string encounterBuildplateId,
        int duration
    )
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum Rarity
        {
            COMMON,
            UNCOMMON,
            RARE,
            EPIC,
            LEGENDARY
        }
    }
}
