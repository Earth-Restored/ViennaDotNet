using System.Diagnostics;
using Solace.Common.Utils;
using Solace.DB.Models.Player.Workshop;
using Solace.StaticData;

namespace Solace.ApiServer.Utils;

public static class CraftingCalculator
{
    public static State CalculateState(long currentTime, CraftingSlot.ActiveJobR activeJob, Catalog catalog)
    {
        Catalog.RecipesCatalogR.CraftingRecipe recipe = catalog.RecipesCatalog.Crafting.Where(craftingRecipe => craftingRecipe.Id == activeJob.RecipeId).First();

        long roundDuration = recipe.Duration * 1000;
        int completedRounds = activeJob.FinishedEarly ? activeJob.TotalRounds : int.Min((int)((currentTime - activeJob.StartTime) / roundDuration), activeJob.TotalRounds);
        int availableRounds = completedRounds - activeJob.CollectedRounds;

        LinkedList<InputItem> input = [];
        if (activeJob.Input.Length != recipe.Ingredients.Length)
        {
            throw new InvalidOperationException();
        }

        for (int index = 0; index < recipe.Ingredients.Length; index++)
        {
            int usedCount = recipe.Ingredients[index].Count * completedRounds;
            InputItem[] inputItems = activeJob.Input[index];
            foreach (InputItem inputItem in inputItems)
            {
                if (usedCount == 0)
                {
                    input.AddLast(inputItem);
                }
                else if (usedCount > inputItem.Count)
                {
                    usedCount -= inputItem.Count;
                }
                else
                {
                    if (inputItem.Instances.Length > 0)
                    {
                        if (inputItem.Instances.Length != inputItem.Count)
                        {
                            throw new UnreachableException();
                        }

                        input.AddLast(new InputItem(inputItem.Id, inputItem.Count - usedCount, ArrayExtensions.CopyOfRange(inputItem.Instances, usedCount, inputItem.Instances.Length)));
                    }
                    else
                    {
                        input.AddLast(new InputItem(inputItem.Id, inputItem.Count - usedCount, []));
                    }

                    usedCount = 0;
                }
            }
        }

        return new State(
            completedRounds,
            availableRounds,
            activeJob.TotalRounds,
            [.. input],
            new State.OutputItem(recipe.Output.ItemId, recipe.Output.Count),
            activeJob.StartTime + roundDuration * (completedRounds + 1),
            activeJob.StartTime + roundDuration * activeJob.TotalRounds,
            completedRounds == activeJob.TotalRounds
        );
    }

    public sealed record State(
        int CompletedRounds,
        int AvailableRounds,
        int TotalRounds,
        InputItem[] Input,
        State.OutputItem Output,
        long NextCompletionTime,
        long TotalCompletionTime,
        bool Completed
    )
    {
        public sealed record OutputItem(
            string Id,
            int Count
        );
    }

    // TODO: make this configurable
    public static FinishPrice CalculateFinishPrice(int remainingTime)
    {
        if (remainingTime < 0)
        {
            throw new ArgumentException($"{nameof(remainingTime)} is negative.", nameof(remainingTime));
        }

        int periods = remainingTime / 10000;
        if (remainingTime % 10000 > 0)
        {
            periods = periods + 1;
        }

        int price = periods * 5;
        int changesAt = (periods - 1) * 10000;
        int validFor = remainingTime - changesAt;

        return new FinishPrice(price, validFor);
    }

    public sealed record FinishPrice(
        int Price,
        int ValidFor
    );

    // TODO: make this configurable
    public static int CalculateUnlockPrice(int slotIndex)
        => slotIndex < 1 || slotIndex > 3
        ? throw new ArgumentOutOfRangeException(nameof(slotIndex))
        : slotIndex * 5;
}
