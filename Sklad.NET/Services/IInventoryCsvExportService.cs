using Sklad.Models;

namespace Sklad.Services;

public interface IInventoryCsvExportService
{
    byte[] Export(IEnumerable<Tire> tires);
}
