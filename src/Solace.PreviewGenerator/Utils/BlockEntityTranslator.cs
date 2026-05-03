using Serilog;
using SharpNBT;
using Solace.PreviewGenerator.BlockEntity;
using Solace.PreviewGenerator.NBT;
using Solace.PreviewGenerator.Registry;

namespace Solace.PreviewGenerator.Utils;

public static class BlockEntityTranslator
{
    public static NbtMap? TranslateBlockEntity(JavaBlocks.BedrockMapping.BlockEntityR blockEntityMapping, BlockEntityInfo? javaBlockEntityInfo)
    {
        switch (blockEntityMapping.Type)
        {
            case "bed":
                {
                    NbtMapBuilder builder = NbtMap.Builder();
                    builder.PutString("id", "Bed");
                    builder.PutByte("color", ((JavaBlocks.BedrockMapping.BedBlockEntity)blockEntityMapping).Color switch
                    {
                        "white" => 0,
                        "orange" => 1,
                        "magenta" => 2,
                        "light_blue" => 3,
                        "yellow" => 4,
                        "lime" => 5,
                        "pink" => 6,
                        "gray" => 7,
                        "light_gray" => 8,
                        "cyan" => 9,
                        "purple" => 10,
                        "blue" => 11,
                        "brown" => 12,
                        "green" => 13,
                        "red" => 14,
                        "black" => 15,
                        _ => 0
                    });
                    return builder.Build();
                }
            case "flower_pot":
                {
                    NbtMapBuilder builder = NbtMap.Builder();
                    builder.PutString("id", "FlowerPot");
                    NbtMap? contents = ((JavaBlocks.BedrockMapping.FlowerPotBlockEntity)blockEntityMapping).Contents;
                    if (contents is not null)
                    {
                        builder.PutCompound("PlantBlock", contents);
                    }

                    return builder.Build();
                }
            case "moving_block":
                {
                    NbtMapBuilder builder = NbtMap.Builder();

                    builder.PutString("id", "MovingBlock");

                    if (javaBlockEntityInfo is null)
                    {
                        Log.Debug("Not sending moving block entity data until server provides data");
                        return null;
                    }

                    CompoundTag javaNbt = javaBlockEntityInfo.Nbt!;

                    if (!javaNbt.ContainsKey("blockStateId"))
                    {
                        Log.Warning("Moving block entity data did not contain numeric block state ID");
                        return null;
                    }

                    int javaBlockId = javaNbt.Get<IntTag>("blockStateId").Value;
                    JavaBlocks.BedrockMapping? bedrockMapping = JavaBlocks.GetBedrockMapping(javaBlockId);
                    if (bedrockMapping is null)
                    {
                        Log.Warning($"Moving block entity contained block with no mapping {JavaBlocks.GetName(javaBlockId)}");
                        return null;
                    }

                    NbtMapBuilder movingBlockBuilder = NbtMap.Builder();
                    movingBlockBuilder.PutString("name", BedrockBlocks.GetName(bedrockMapping.Id)!);
                    movingBlockBuilder.PutCompound("states", BedrockBlocks.GetStateNbt(bedrockMapping.Id)!);
                    builder.PutCompound("movingBlock", movingBlockBuilder.Build());

                    if (bedrockMapping.Waterlogged)
                    {
                        NbtMapBuilder movingBlockExtraBuilder = NbtMap.Builder();
                        movingBlockExtraBuilder.PutString("name", BedrockBlocks.GetName(BedrockBlocks.WaterId)!);
                        movingBlockExtraBuilder.PutCompound("states", BedrockBlocks.GetStateNbt(BedrockBlocks.WaterId)!);
                        builder.PutCompound("movingBlockExtra", movingBlockExtraBuilder.Build());
                    }

                    if (bedrockMapping.BlockEntity is not null)
                    {
                        NbtMap? blockEntityNbt = BlockEntityTranslator.TranslateBlockEntity(bedrockMapping.BlockEntity, null);
                        if (blockEntityNbt is not null)
                        {
                            builder.PutCompound("movingEntity", blockEntityNbt.ToBuilder().PutInt("x", javaBlockEntityInfo.X).PutInt("y", javaBlockEntityInfo.Y).PutInt("z", javaBlockEntityInfo.Z).PutBoolean("isMovable", false).Build());
                        }
                    }

                    if (!javaNbt.ContainsKey("basePos"))
                    {
                        Log.Warning("Moving block entity data did not contain piston base position");
                        return null;
                    }

                    CompoundTag basePosTag = javaNbt.Get<CompoundTag>("basePos");
                    builder.PutInt("pistonPosX", basePosTag.Get<IntTag>("x").Value);
                    builder.PutInt("pistonPosY", basePosTag.Get<IntTag>("y").Value);
                    builder.PutInt("pistonPosZ", basePosTag.Get<IntTag>("z").Value);

                    return builder.Build();
                }
            case "piston":
                {
                    var pistonBlockEntity = (JavaBlocks.BedrockMapping.PistonBlockEntity)blockEntityMapping;
                    NbtMapBuilder builder = NbtMap.Builder();
                    builder.PutString("id", "PistonArm");
                    builder.PutByte("State", (byte)(pistonBlockEntity.Extended ? 2 : 0));
                    builder.PutByte("NewState", (byte)(pistonBlockEntity.Extended ? 2 : 0));
                    builder.PutFloat("Progress", pistonBlockEntity.Extended ? 1.0f : 0.0f);
                    builder.PutFloat("LastProgress", pistonBlockEntity.Extended ? 1.0f : 0.0f);
                    builder.PutBoolean("Sticky", pistonBlockEntity.Sticky);
                    return builder.Build();
                }
            default:
                throw new InvalidOperationException();
        }
    }
}
