using System.Text.Json.Serialization;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Global;

public sealed class SharedBuildplates
{
    [JsonInclude, JsonPropertyName("sharedBuildplates")]
    public readonly Dictionary<string, SharedBuildplate> _sharedBuildplates = [];

    public void AddSharedBuildplate(string id, SharedBuildplate buildplate)
        => _sharedBuildplates[id] = buildplate;

    public SharedBuildplate? GetSharedBuildplate(string id)
        => _sharedBuildplates.GetOrDefault(id);

    public sealed class SharedBuildplate
    {
        public string PlayerId { get; }

        public int Size { get; }
        public int Offset { get; }
        public int Scale { get; }

        public bool Night { get; }

        public long Created { get; }
        public long BuildplateLastModifed { get; }
        public long LastViewed { get; set; }
        public int NumberOfTimesViewed { get; set; }

        public HotbarItem?[] Hotbar { get; }

        public string ServerDataObjectId { get; }

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
