using System.Diagnostics;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player.Workshop;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Utils;

public static class SmeltingCalculator
{
    public static State CalculateState(long currentTime, SmeltingSlot.ActiveJobR activeJob, SmeltingSlot.BurningR? burning, Catalog catalog)
    {
        Catalog.RecipesCatalogR.SmeltingRecipe? recipe = catalog.RecipesCatalog.GetSmeltingRecipe(activeJob.RecipeId);
        Debug.Assert(recipe is not null);

        int totalHeatRequired = recipe.HeatRequired * activeJob.TotalRounds;
        long totalCompletionTime = activeJob.StartTime + CalculateDurationForHeat(totalHeatRequired, burning, activeJob.AddedFuel);
        long nextCompletionTime = 0;
        int completedRounds;
        if (activeJob.FinishedEarly)
        {
            completedRounds = activeJob.TotalRounds;
        }
        else
        {
            for (completedRounds = 0; completedRounds < activeJob.TotalRounds; completedRounds++)
            {
                nextCompletionTime = activeJob.StartTime + CalculateDurationForHeat(recipe.HeatRequired * (completedRounds + 1), burning, activeJob.AddedFuel);
                if (nextCompletionTime >= currentTime)
                {
                    break;
                }
            }
        }

        if (completedRounds < activeJob.TotalRounds && nextCompletionTime == 0)
        {
            throw new InvalidOperationException();
        }

        int availableRounds = completedRounds - activeJob.CollectedRounds;
        bool completed = completedRounds == activeJob.TotalRounds;

        InputItem input;
        if (activeJob.Input.Count != activeJob.TotalRounds)
        {
            throw new InvalidOperationException();
        }

        if (activeJob.Input.Instances.Length > 0)
        {
            if (activeJob.Input.Instances.Length != activeJob.Input.Count)
            {
                throw new InvalidOperationException();
            }

            input = new InputItem(activeJob.Input.Id, activeJob.Input.Count - completedRounds, ArrayExtensions.CopyOfRange(activeJob.Input.Instances, completedRounds, activeJob.Input.Instances.Length));
        }
        else
        {
            input = new InputItem(activeJob.Input.Id, activeJob.Input.Count - completedRounds, []);
        }

        int consumedAddedFuelCount = 0;
        long fuelEndTime = completed ? totalCompletionTime : currentTime;
        SmeltingSlot.Fuel currentFuel;
        int currentFuelTotalHeat;
        long burnStartTime;
        long burnEndTime;

        if (burning is not null)
        {
            currentFuel = burning.Fuel;
            currentFuelTotalHeat = burning.RemainingHeat;
            burnStartTime = activeJob.StartTime;
            burnEndTime = burnStartTime + burning.RemainingHeat * 1000 / burning.Fuel.HeatPerSecond;
        }
        else
        {
            if (activeJob.AddedFuel is null)
            {
                throw new InvalidOperationException();
            }

            currentFuel = activeJob.AddedFuel;
            consumedAddedFuelCount = 1;
            currentFuelTotalHeat = currentFuel.HeatPerSecond * currentFuel.BurnDuration;
            burnStartTime = activeJob.StartTime;
            burnEndTime = burnStartTime + currentFuel.BurnDuration * 1000;
        }

        while (burnEndTime < fuelEndTime)
        {
            if (activeJob.AddedFuel is null)
            {
                throw new InvalidOperationException();
            }

            totalHeatRequired -= currentFuelTotalHeat;
            currentFuel = activeJob.AddedFuel;
            consumedAddedFuelCount++;
            currentFuelTotalHeat = currentFuel.HeatPerSecond * currentFuel.BurnDuration;
            burnStartTime = burnEndTime;
            burnEndTime = burnStartTime + currentFuel.BurnDuration * 1000;
        }

        if (totalHeatRequired < 0)
        {
            throw new InvalidOperationException();
        }

        int remainingHeat;
        if (!completed)
        {
            remainingHeat = (int)(currentFuelTotalHeat * (burnEndTime - fuelEndTime)) / (currentFuel.BurnDuration * 1000);
        }
        else
        {
            if (totalHeatRequired > currentFuelTotalHeat)
            {
                throw new InvalidOperationException();
            }

            remainingHeat = currentFuelTotalHeat - totalHeatRequired;
        }

        SmeltingSlot.Fuel? remainingAddedFuel;
        if (activeJob.AddedFuel is null)
        {
            if (consumedAddedFuelCount > 0)
            {
                throw new InvalidOperationException();
            }

            remainingAddedFuel = null;
        }
        else
        {
            if (consumedAddedFuelCount > activeJob.AddedFuel.Item.Count)
            {
                throw new InvalidOperationException();
            }

            if (activeJob.AddedFuel.Item.Instances.Length > 0)
            {
                if (activeJob.AddedFuel.Item.Instances.Length != activeJob.AddedFuel.Item.Count)
                {
                    throw new InvalidOperationException();
                }

                remainingAddedFuel = new SmeltingSlot.Fuel(new InputItem(activeJob.AddedFuel.Item.Id, activeJob.AddedFuel.Item.Count - consumedAddedFuelCount, ArrayExtensions.CopyOfRange(activeJob.AddedFuel.Item.Instances, consumedAddedFuelCount, activeJob.AddedFuel.Item.Instances.Length)), activeJob.AddedFuel.BurnDuration, activeJob.AddedFuel.HeatPerSecond);
            }
            else
            {
                remainingAddedFuel = new SmeltingSlot.Fuel(new InputItem(activeJob.AddedFuel.Item.Id, activeJob.AddedFuel.Item.Count - consumedAddedFuelCount, []), activeJob.AddedFuel.BurnDuration, activeJob.AddedFuel.HeatPerSecond);
            }
        }

        SmeltingSlot.Fuel currentBurningFuel;
        if (consumedAddedFuelCount > 0)
        {
            if (activeJob.AddedFuel!.Item.Instances.Length > 0)
            {
                currentBurningFuel = new SmeltingSlot.Fuel(new InputItem(activeJob.AddedFuel.Item.Id, 1, [activeJob.AddedFuel.Item.Instances[consumedAddedFuelCount - 1]]), activeJob.AddedFuel.BurnDuration, activeJob.AddedFuel.HeatPerSecond);
            }
            else
            {
                currentBurningFuel = new SmeltingSlot.Fuel(new InputItem(activeJob.AddedFuel.Item.Id, 1, []), activeJob.AddedFuel.BurnDuration, activeJob.AddedFuel.HeatPerSecond);
            }
        }
        else
        {
            currentBurningFuel = currentFuel;
        }

        return new State(
            completedRounds,
            availableRounds,
            activeJob.TotalRounds,
            input,
            new State.OutputItem(recipe.Output, 1),
            nextCompletionTime,
            totalCompletionTime,
            remainingAddedFuel,
            currentBurningFuel,
            remainingHeat,
            burnStartTime,
            burnEndTime,
            completed
        );
    }

    private static int CalculateDurationForHeat(int requiredHeat, SmeltingSlot.BurningR? burning, SmeltingSlot.Fuel? addedFuel)
    {
        int duration = 0;
        if (burning is not null)
        {
            if (burning.RemainingHeat >= requiredHeat)
            {
                duration += requiredHeat * 1000 / burning.Fuel.HeatPerSecond;
                requiredHeat = 0;
            }
            else
            {
                duration += burning.RemainingHeat * 1000 / burning.Fuel.HeatPerSecond;
                requiredHeat -= burning.RemainingHeat;
            }
        }

        if (addedFuel is not null)
        {
            for (int count = 0; count < addedFuel.Item.Count; count++)
            {
                if (requiredHeat < addedFuel.HeatPerSecond * addedFuel.BurnDuration)
                {
                    duration += requiredHeat * 1000 / addedFuel.HeatPerSecond;
                    requiredHeat = 0;
                    break;
                }
                else
                {
                    duration += addedFuel.BurnDuration * 1000;
                    requiredHeat -= addedFuel.HeatPerSecond * addedFuel.BurnDuration;
                }
            }
        }

        if (requiredHeat > 0)
        {
            throw new InvalidOperationException();
        }

        return duration;
    }

    public sealed record State(
        int CompletedRounds,
        int AvailableRounds,
        int TotalRounds,
        InputItem Input,
        State.OutputItem Output,
        long NextCompletionTime,
        long TotalCompletionTime,
        SmeltingSlot.Fuel? RemainingAddedFuel,
        SmeltingSlot.Fuel CurrentBurningFuel,
        int RemainingHeat,
        long BurnStartTime,
        long BurnEndTime,
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
            throw new ArgumentException(nameof(remainingTime));
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
    {
        if (slotIndex < 1 || slotIndex > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        return slotIndex * 5;
    }
}
