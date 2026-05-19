using Solace.Common.Utils;

namespace Solace.DB.Models.Global;

public sealed class Tile : IEntityWithId<ulong>, IMergeable<Tile>
{
    public ulong Id { get; set; }

    public required string ObjectStoreId { get; set; }

    public async Task MergeWith(Tile other, ValueMerger merger)
    {
        merger.CurrentUserId = null;
        merger.CurrentUsername = null;

        if (ObjectStoreId != other.ObjectStoreId)
        {
            ObjectStoreId = other.ObjectStoreId;
        }
    }
}