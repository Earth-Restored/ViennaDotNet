using System.Diagnostics;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player.Workshop;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Utils;

public static class CraftingCalculator
{
    public static State calculateState(long currentTime, CraftingSlot.ActiveJob activeJob, Catalog catalog)
    {
        Catalog.RecipesCatalog.CraftingRecipe recipe = catalog.recipesCatalog.crafting.Where(craftingRecipe => craftingRecipe.id == activeJob.recipeId).First();

        long roundDuration = recipe.duration * 1000;
        int completedRounds = activeJob.finishedEarly ? activeJob.totalRounds : int.Min((int)((currentTime - activeJob.startTime) / roundDuration), activeJob.totalRounds);
        int availableRounds = completedRounds - activeJob.collectedRounds;

        LinkedList<InputItem> input = [];
        if (activeJob.input.Length != recipe.ingredients.Length)
            throw new InvalidOperationException();

        for (int index = 0; index < recipe.ingredients.Length; index++)
        {
            int usedCount = recipe.ingredients[index].count * completedRounds;
            InputItem[] inputItems = activeJob.input[index];
            foreach (InputItem inputItem in inputItems)
            {
                if (usedCount == 0)
                {
                    input.AddLast(inputItem);
                }
                else if (usedCount > inputItem.count)
                {
                    usedCount -= inputItem.count;
                }
                else
                {
                    if (inputItem.instances.Length > 0)
                    {
                        if (inputItem.instances.Length != inputItem.count)
                        {
                            throw new UnreachableException();
                        }

                        input.AddLast(new InputItem(inputItem.id, inputItem.count - usedCount, ArrayExtensions.CopyOfRange(inputItem.instances, usedCount, inputItem.instances.Length)));
                    }
                    else
                    {
                        input.AddLast(new InputItem(inputItem.id, inputItem.count - usedCount, []));
                    }
                    usedCount = 0;
                }
            }
        }

        return new State(
            completedRounds,
            availableRounds,
            activeJob.totalRounds,
            [.. input],
            new State.OutputItem(recipe.output.itemId, recipe.output.count),
            activeJob.startTime + roundDuration * (completedRounds + 1),
            activeJob.startTime + roundDuration * activeJob.totalRounds,
            completedRounds == activeJob.totalRounds
        );
    }

    public record State(
        int completedRounds,
        int availableRounds,
        int totalRounds,
        InputItem[] input,
        State.OutputItem output,
        long nextCompletionTime,
        long totalCompletionTime,
        bool completed
    )
    {
        public record OutputItem(
            string id,
            int count
        );
    }

    // TODO: make this configurable
    public static FinishPrice calculateFinishPrice(int remainingTime)
    {
        if (remainingTime < 0)
            throw new ArgumentException(nameof(remainingTime));

        int periods = remainingTime / 10000;
        if (remainingTime % 10000 > 0)
            periods = periods + 1;

        int price = periods * 5;
        int changesAt = (periods - 1) * 10000;
        int validFor = remainingTime - changesAt;

        return new FinishPrice(price, validFor);
    }

    public record FinishPrice(
        int price,
        int validFor
    )
    {
    }

    // TODO: make this configurable
    public static int calculateUnlockPrice(int slotIndex)
    {
        if (slotIndex < 1 || slotIndex > 3)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));

        return slotIndex * 5;
    }
}
