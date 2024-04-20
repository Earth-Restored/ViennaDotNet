using Newtonsoft.Json;
using Serilog;
using System.IO.Pipelines;
using ViennaDotNet.ApiServer.Types.Catalog;

namespace ViennaDotNet.ApiServer
{
    public sealed class Catalog
    {
        public ItemsCatalog itemsCatalog;
        public RecipesCatalog recipesCatalog;
        public JournalCatalog journalCatalog;
        public NFCBoost[] nfcBoostsCatalog;

        public Catalog()
        {
            // TODO: use own data format rather than using the Project Earth files
            try
            {
                Log.Information("Loading catalog data");
                string catalogDataDir = Path.Combine("data", "catalog");
                LinkedList<ItemsCatalog.Item> items = new();
                Dictionary<string, ItemsCatalog.EfficiencyCategory> efficiencyCategories = new();
                foreach (string file in Directory.EnumerateFiles(Path.Combine(catalogDataDir, "items")))
                {
                    items.AddLast(JsonConvert.DeserializeObject<ItemsCatalog.Item>(File.ReadAllText(file))!);
                }

                foreach (string file in Directory.EnumerateFiles(Path.Combine(catalogDataDir, "efficiency_categories")))
                {
                    // was just: name.replace(".json", "");
                    string name = Path.GetFileNameWithoutExtension(file);
                    ItemsCatalog.EfficiencyCategory.EfficiencyMap efficiencyMap = JsonConvert.DeserializeObject<ItemsCatalog.EfficiencyCategory.EfficiencyMap>(File.ReadAllText(file))!;
                    efficiencyCategories.Add(name, new ItemsCatalog.EfficiencyCategory(efficiencyMap));
                }

                itemsCatalog = new ItemsCatalog(items.ToArray(), efficiencyCategories);

                recipesCatalog = JsonConvert.DeserializeObject<RecipesCatalogFile>(File.ReadAllText(Path.Combine(catalogDataDir, "recipes.json")))!.result;
                journalCatalog = JsonConvert.DeserializeObject<JournalCatalogFile>(File.ReadAllText(Path.Combine(catalogDataDir, "journalCatalog.json")))!.result;
                nfcBoostsCatalog = JsonConvert.DeserializeObject<NFCBoostsCatalogFile>(File.ReadAllText(Path.Combine(catalogDataDir, "productCatalog.json")))!.result;
            }
            catch (Exception exception)
            {
                Log.Fatal("Failed to load catalog data", exception);
                Environment.Exit(1);
                throw new InvalidOperationException();
            }
        }

        record RecipesCatalogFile(RecipesCatalog result)
        {
        }
        record JournalCatalogFile(JournalCatalog result)
        {
        }
        record NFCBoostsCatalogFile(NFCBoost[] result)
        {
        }
    }
}
