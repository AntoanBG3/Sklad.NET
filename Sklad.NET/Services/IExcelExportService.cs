using Sklad.Models;

namespace Sklad.Services;

public interface IExcelExportService
{
    byte[] ExportTires(IEnumerable<Tire> tires);

    byte[] ExportMovements(IEnumerable<StockMovement> movements);
}
