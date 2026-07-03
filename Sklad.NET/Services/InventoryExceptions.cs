namespace Sklad.Services;

public abstract class InventoryException : Exception
{
    protected InventoryException(string message) : base(message) { }
}

public sealed class TireNotFoundException : InventoryException
{
    public TireNotFoundException() : base("Tire not found.") { }
}

public sealed class DuplicateSkuException : InventoryException
{
    public string Sku { get; }

    public DuplicateSkuException(string sku) : base($"A tire with SKU '{sku}' already exists.")
        => Sku = sku;
}

public sealed class TireHasMovementsException : InventoryException
{
    public TireHasMovementsException() : base("The tire has movement records and cannot be deleted.") { }
}

public sealed class InsufficientStockException : InventoryException
{
    public int Available { get; }
    public int Requested { get; }

    public InsufficientStockException(int available, int requested)
        : base($"Insufficient stock. Available: {available}, requested: {requested}.")
    {
        Available = available;
        Requested = requested;
    }
}

public sealed class InvalidMovementQuantityException : InventoryException
{
    public InvalidMovementQuantityException() : base("Quantity must be at least 1 for In/Out movements and non-negative for adjustments.") { }
}

public sealed class StaleTireException : InventoryException
{
    public StaleTireException() : base("The tire was modified by someone else.") { }
}
