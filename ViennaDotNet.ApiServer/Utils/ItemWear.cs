using ViennaDotNet.ApiServer.Types.Catalog;

namespace ViennaDotNet.ApiServer.Utils
{
    public static class ItemWear
    {
        public static float wearToHealth(string itemId, int wear, ItemsCatalog itemsCatalog)
        {
            ItemsCatalog.Item catalogItem = itemsCatalog.items.Where(item=>item.id==itemId).First();
#pragma warning disable CS8629 // Nullable value type may be null.
            return ((float)(catalogItem.item.health - wear) / (float)catalogItem.item.health) * 100.0f;
#pragma warning restore CS8629 // Nullable value type may be null.
        }
    }
}
