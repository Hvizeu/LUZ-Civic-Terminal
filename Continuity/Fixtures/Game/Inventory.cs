namespace Nivalis.InventorySystem
{
    public sealed class ItemType { }
    public readonly record struct ItemInstanceData(int Count);
    public enum ItemCollectionOperationResult { Failed = 0, Partial = 1, Succeeded = 2 }
    public sealed class ItemContainer
    {
        public int Count { get; private set; }
        public int Attempts { get; private set; }
        public ItemCollectionOperationResult TryAdd(ItemInstanceData item)
        {
            Attempts++;
            if (item.Count < 0) throw new InvalidOperationException("native insertion rejected");
            if (item.Count == 0) return ItemCollectionOperationResult.Failed;
            Count += item.Count; return ItemCollectionOperationResult.Succeeded;
        }
    }
}
namespace Nivalis.Economy
{
    public sealed class EconomyManager { public sealed class VendorItemDefinition { } }
    public sealed class Vendor
    {
        public int Registered { get; private set; }
        private void TryRegisterItem(EconomyManager.VendorItemDefinition definition, Nivalis.InventorySystem.ItemType item) => Registered++;
    }
}
