using System.Text.Json.Serialization;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Global;

public sealed class SharedBuildplates
{
    [JsonInclude, JsonPropertyName("sharedBuildplates")]
    public Dictionary<string, SharedBuildplate> _sharedBuildplates = [];

    public void AddSharedBuildplate(string id, SharedBuildplate buildplate)
        => _sharedBuildplates[id] = buildplate;

    public SharedBuildplate? GetSharedBuildplate(string id)
        => _sharedBuildplates.GetOrDefault(id);

    public sealed class SharedBuildplate
    {
        public string PlayerId { get; init; }

        public int Size { get; init; }
        public int Offset { get; init; }
        public int Scale { get; init; }

        public bool Night { get; init; }

        public long Created { get; init; }
        public long BuildplateLastModifed { get; init; }
        public long LastViewed { get; set; }
        public int NumberOfTimesViewed { get; set; }

        public HotbarItem?[] Hotbar { get; init; }

        public string ServerDataObjectId { get; init; }

        public SharedBuildplate(string playerId, int size, int offset, int scale, bool night, long created, long buildplateLastModifed, string serverDataObjectId)
        {
            PlayerId = playerId;

            Size = size;
            Offset = offset;
            Scale = scale;

            Night = night;

            Created = created;
            BuildplateLastModifed = buildplateLastModifed;
            LastViewed = 0;
            NumberOfTimesViewed = 0;

            Hotbar = new HotbarItem[7];

            ServerDataObjectId = serverDataObjectId;
        }

        public sealed record HotbarItem(
            string Uuid,
            int Count,
            string? InstanceId,
            int Wear
        );
    }
}
