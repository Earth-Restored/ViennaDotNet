namespace ViennaDotNet.ApiServer.Types.Catalog;

public sealed record RecipesCatalog(
    RecipesCatalog.CraftingRecipe[] Crafting,
    RecipesCatalog.SmeltingRecipe[] Smelting
)
{
    public sealed record CraftingRecipe(
        string Id,
        string Category,
        string Duration,
        CraftingRecipe.Ingredient[] Ingredients,
        CraftingRecipe.OutputR Output,
        CraftingRecipe.ReturnItem[] ReturnItems,
        bool Deprecated
    )
    {
        public sealed record Ingredient(
            string[] Items,
            int Quantity
        );

        public sealed record OutputR(
            string ItemId,
            int Quantity
        );

        public sealed record ReturnItem(
            string Id,
            int Amount
        );
    }

    public sealed record SmeltingRecipe(
        string Id,
        int HeatRequired,
        string InputItemId,
        SmeltingRecipe.OutputR Output,
        SmeltingRecipe.ReturnItem[] ReturnItems,
        bool deprecated
    )
    {
        public sealed record OutputR(
            string ItemId,
            int Quantity
        );

        public sealed record ReturnItem(
            string Id,
            int Amount
        );
    }
}
