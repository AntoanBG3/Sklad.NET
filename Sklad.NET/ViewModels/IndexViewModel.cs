using Sklad.Models;
using Sklad.Services;

namespace Sklad.ViewModels;

public class IndexViewModel
{
    public required PagedResult<Tire> Results { get; init; }
    public required TireFilterViewModel Filter { get; init; }
    public required WarehouseStats Stats { get; init; }
}
