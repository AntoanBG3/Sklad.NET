using Sklad.Models;

namespace Sklad.ViewModels;

public class IndexViewModel
{
    public IEnumerable<Tire> Tires { get; set; } = [];
    public TireFilterViewModel Filter { get; set; } = new();
    public int TotalSkus { get; set; }
    public int TotalUnits { get; set; }
    public int LowStockCount { get; set; }
    public decimal TotalValue { get; set; }
}
