using Sklad.Models;

namespace Sklad.ViewModels;

public class FloorBookViewModel
{
    public required int TireId { get; init; }
    public required string Sku { get; init; }
    public required string Description { get; init; }
    public required string Size { get; init; }
    public string? Location { get; init; }
    public required int Quantity { get; init; }

    public static FloorBookViewModel FromTire(Tire tire) => new()
    {
        TireId = tire.Id,
        Sku = tire.Sku,
        Description = $"{tire.Brand} {tire.Model}",
        Size = tire.Size,
        Location = tire.Location,
        Quantity = tire.Quantity
    };
}
