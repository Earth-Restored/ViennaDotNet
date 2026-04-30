namespace Solace.PreviewGenerator.BlockEntity;

public enum BlockEntityType : int
{
#pragma warning disable CA1707 // Identifiers should not contain underscores
    FURNACE,
    CHEST,
    TRAPPED_CHEST,
    ENDER_CHEST,
    JUKEBOX,
    DISPENSER,
    DROPPER,
    SIGN,
    HANGING_SIGN,
    MOB_SPAWNER,
    PISTON,
    BREWING_STAND,
    ENCHANTING_TABLE,
    END_PORTAL,
    BEACON,
    SKULL,
    DAYLIGHT_DETECTOR,
    HOPPER,
    COMPARATOR,
    BANNER,
    STRUCTURE_BLOCK,
    END_GATEWAY,
    COMMAND_BLOCK,
    SHULKER_BOX,
    BED,
    CONDUIT,
    BARREL,
    SMOKER,
    BLAST_FURNACE,
    LECTERN,
    BELL,
    JIGSAW,
    CAMPFIRE,
    BEEHIVE,
    SCULK_SENSOR,
    CALIBRATED_SCULK_SENSOR,
    SCULK_CATALYST,
    SCULK_SHRIEKER,
    CHISELED_BOOKSHELF,
    BRUSHABLE_BLOCK,
    DECORATED_POT,
    CRAFTER,
    TRIAL_SPAWNER
#pragma warning restore CA1707 // Identifiers should not contain underscores
}

public static class BlockEntityTypeE
{
    private static readonly BlockEntityType[] VALUES = Enum.GetValues<BlockEntityType>();

    public static BlockEntityType? From(int id)
    {
        if (id >= 0 && id < VALUES.Length)
        {
            return VALUES[id];
        }
        else
        {
            return null;
        }
    }
}
