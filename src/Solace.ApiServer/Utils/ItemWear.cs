using Serilog;
using Solace.StaticData;

namespace Solace.ApiServer.Utils;

public static class ItemWear
{
    public static float WearToHealth(string itemId, int wear, Catalog.ItemsCatalogR itemsCatalog)
    {
        Catalog.ItemsCatalogR.Item? catalogItem = itemsCatalog.GetItem(itemId);

        if (catalogItem is null || catalogItem.ToolInfo is null)
        {
            Log.Warning("Attempt to get item health for non-tool item {}", itemId);
            return 100.0f;
        }

        return ((catalogItem.ToolInfo.MaxWear - wear) / (float)catalogItem.ToolInfo.MaxWear) * 100.0f;
    }
}
