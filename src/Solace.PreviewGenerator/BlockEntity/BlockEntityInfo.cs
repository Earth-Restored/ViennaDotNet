using SharpNBT;

namespace ViennaDotNet.PreviewGenerator.BlockEntity;

public class BlockEntityInfo
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;
    public readonly BlockEntityType Type;
    public readonly CompoundTag? Nbt;

    public BlockEntityInfo(int x, int y, int z, BlockEntityType type, CompoundTag? nbt)
    {
        X = x;
        Y = y;
        Z = z;
        Type = type;
        Nbt = nbt;
    }
}
