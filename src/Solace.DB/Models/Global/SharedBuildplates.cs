using System.Diagnostics;
using System.Text.Json.Serialization;
using Solace.Common.Utils;

namespace Solace.DB.Models.Global;

public sealed class LegacySharedBuildplates
{
    [JsonInclude, JsonPropertyName("sharedBuildplates")]
    public Dictionary<string, SharedBuildplate> SharedBuildplates = [];

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

public sealed class SharedBuildplateEF : IEntityWithId<Guid>, IMergeable<SharedBuildplateEF>
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public required int Size { get; set; }

    public required int Offset { get; set; }

    public required int Scale { get; set; }

    public required bool Night { get; set; }

    public required long Created { get; set; }

    public required long BuildplateLastModifed { get; set; }

    public required long LastViewed { get; set; }

    public required int NumberOfTimesViewed { get; set; }

    public HotbarItem?[] Hotbar { get; set; } = new HotbarItem[7];

    public required string ServerDataObjectId { get; set; }

    public async Task MergeWith(SharedBuildplateEF other, ValueMerger merger)
    {
        merger.CurrentUserId = Id.ToString();
        merger.CurrentUsername = Account?.Username;

        // same buildplate
        if (AccountId == other.AccountId && Size == other.Size && Offset == other.Offset && Created == other.Created)
        {
            switch (await merger.PromptMergeConflictAsync(merger.CreateContextForPropertyName($"Shared buildplate '{Id}'"), GetInfoString(), other.GetInfoString(), false))
            {
                case MergeAction.KeepCurrent:
                    break;
                case MergeAction.KeepIncoming:
                    {
                        Scale = other.Scale;
                        Night = other.Night;
                        BuildplateLastModifed = other.BuildplateLastModifed;
                        LastViewed = other.LastViewed;
                        NumberOfTimesViewed = other.NumberOfTimesViewed;
                        Hotbar = other.Hotbar;
                        ServerDataObjectId = other.ServerDataObjectId;
                    }

                    break;
                default:
                    Debug.Fail($"Unexpected value");
                    break;
            }

            return;
        }

        // different buildplate, override
        AccountId = other.AccountId;
        Size = other.Size;
        Offset = other.Offset;
        Scale = other.Scale;
        Night = other.Night;
        Created = other.Created;
        BuildplateLastModifed = other.BuildplateLastModifed;
        LastViewed = other.LastViewed;
        NumberOfTimesViewed = other.NumberOfTimesViewed;
        Hotbar = other.Hotbar;
        ServerDataObjectId = other.ServerDataObjectId;
    }
    
    private string GetInfoString()
        => $"Scale: {Scale}, Night: {Night}, Last modified: {DateTimeOffset.FromUnixTimeMilliseconds(BuildplateLastModifed).UtcDateTime:s}";

    public sealed record HotbarItem(
        string Uuid,
        int Count,
        string? InstanceId,
        int Wear
    );
}
