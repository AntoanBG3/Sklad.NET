namespace Sklad.Services;

public abstract class PurchasingException : Exception
{
    protected PurchasingException(string message) : base(message) { }
}

public sealed class SupplierNotFoundException : PurchasingException
{
    public SupplierNotFoundException() : base("Supplier not found.") { }
}

public sealed class DuplicateSupplierNameException : PurchasingException
{
    public string Name { get; }

    public DuplicateSupplierNameException(string name) : base($"A supplier named '{name}' already exists.")
        => Name = name;
}

public sealed class SupplierHasOrdersException : PurchasingException
{
    public SupplierHasOrdersException() : base("The supplier has purchase orders and cannot be deleted.") { }
}

public sealed class PurchaseOrderNotFoundException : PurchasingException
{
    public PurchaseOrderNotFoundException() : base("Purchase order not found.") { }
}

public sealed class EmptyPurchaseOrderException : PurchasingException
{
    public EmptyPurchaseOrderException() : base("A purchase order needs at least one line.") { }
}

public sealed class InvalidOrderLineException : PurchasingException
{
    public InvalidOrderLineException() : base("Order lines need a quantity of at least 1 and a non-negative cost.") { }
}

public sealed class InvalidOrderStateException : PurchasingException
{
    public Models.PurchaseOrderStatus Status { get; }

    public InvalidOrderStateException(Models.PurchaseOrderStatus status)
        : base($"The order is {status} and does not allow this action.")
        => Status = status;
}

public sealed class StalePurchaseOrderException : PurchasingException
{
    public StalePurchaseOrderException()
        : base("The purchase order was modified by someone else.") { }
}
