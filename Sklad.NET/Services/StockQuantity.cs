namespace Sklad.Services;

/// <summary>
/// Centralizes the storage invariant shared by direct movements and purchase
/// order receipts: stock is a non-negative Int32 and may never wrap around.
/// </summary>
internal static class StockQuantity
{
    public static int Add(int currentQuantity, long addedQuantity)
    {
        var result = (long)currentQuantity + addedQuantity;
        if (addedQuantity < 0 || result is < 0 or > int.MaxValue)
            throw new StockQuantityOverflowException(currentQuantity, addedQuantity);

        return (int)result;
    }
}
