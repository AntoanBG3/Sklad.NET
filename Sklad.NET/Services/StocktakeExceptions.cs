namespace Sklad.Services;

public abstract class StocktakeException(string message) : Exception(message);

public sealed class StocktakeNotFoundException() : StocktakeException("Stocktake not found.");

public sealed class EmptyStocktakeException() : StocktakeException("The selected scope contains no tires.");

public sealed class InvalidStocktakeStateException() : StocktakeException("The stocktake is no longer open.");

public sealed class IncompleteStocktakeException(int remaining)
    : StocktakeException("Every line must be counted before completion.")
{
    public int Remaining { get; } = remaining;
}

public sealed class StaleStocktakeException() : StocktakeException("The stocktake was modified by someone else.");

public sealed class InvalidStocktakeLinesException() : StocktakeException("The submitted count lines are invalid.");

public sealed class ActiveStocktakeExistsException(string number)
    : StocktakeException("One or more tires already belong to an open stocktake.")
{
    public string Number { get; } = number;
}

public sealed class StocktakeInventoryChangedException(IReadOnlyList<string> skus)
    : StocktakeException("Stock changed after the stocktake snapshot was created.")
{
    public IReadOnlyList<string> Skus { get; } = skus;
}
