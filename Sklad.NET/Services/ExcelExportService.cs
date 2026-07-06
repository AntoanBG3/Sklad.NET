using ClosedXML.Excel;
using Microsoft.Extensions.Localization;
using Sklad.Helpers;
using Sklad.Models;

namespace Sklad.Services;

public class ExcelExportService : IExcelExportService
{
    private const string PriceFormat = "#,##0.00";
    private const string DateFormat = "dd.mm.yyyy hh:mm";
    private const double MaxColumnWidth = 60;

    private readonly IStringLocalizer<SharedResource> _l;

    public ExcelExportService(IStringLocalizer<SharedResource> l) => _l = l;

    public byte[] ExportTires(IEnumerable<Tire> tires)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(_l["Inventory"]);

        WriteHeader(ws,
        [
            _l["SKU"], _l["Barcode"], _l["Brand"], _l["Model"], _l["Size"], _l["Season"], _l["Type"],
            _l["Unit Price (EUR)"], _l["Unit Price (BGN)"], _l["Quantity"], _l["Min Stock"],
            _l["Stock Value (EUR)"], _l["Location"]
        ]);

        var row = 2;
        foreach (var t in tires)
        {
            ws.Cell(row, 1).Value = t.Sku;
            ws.Cell(row, 2).Value = t.Barcode ?? "";
            ws.Cell(row, 3).Value = t.Brand;
            ws.Cell(row, 4).Value = t.Model;
            ws.Cell(row, 5).Value = t.Size;
            ws.Cell(row, 6).Value = _l[Enums.Key(t.Season)].Value;
            ws.Cell(row, 7).Value = _l[Enums.Key(t.Type)].Value;
            ws.Cell(row, 8).Value = t.UnitPrice;
            ws.Cell(row, 9).Value = Math.Round(t.UnitPrice * Money.BgnPerEur, 2, MidpointRounding.AwayFromZero);
            ws.Cell(row, 10).Value = t.Quantity;
            ws.Cell(row, 11).Value = t.MinStock;
            ws.Cell(row, 12).Value = t.Quantity * t.UnitPrice;
            ws.Cell(row, 13).Value = t.Location ?? "";
            row++;
        }

        ws.Column(8).Style.NumberFormat.Format = PriceFormat;
        ws.Column(9).Style.NumberFormat.Format = PriceFormat;
        ws.Column(12).Style.NumberFormat.Format = PriceFormat;

        Finish(ws);
        return Save(workbook);
    }

    public byte[] ExportMovements(IEnumerable<StockMovement> movements)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(_l["Movements"]);

        WriteHeader(ws,
        [
            _l["Date"], _l["SKU"], _l["Brand"], _l["Model"], _l["Movement Type"],
            _l["Quantity"], _l["User"], _l["Note"]
        ]);

        var row = 2;
        foreach (var m in movements)
        {
            ws.Cell(row, 1).Value = Dates.Shop(m.Date);
            ws.Cell(row, 2).Value = m.Tire.Sku;
            ws.Cell(row, 3).Value = m.Tire.Brand;
            ws.Cell(row, 4).Value = m.Tire.Model;
            ws.Cell(row, 5).Value = _l[Enums.Key(m.MovementType)].Value;
            ws.Cell(row, 6).Value = m.MovementType == MovementType.Out ? -m.Quantity : m.Quantity;
            ws.Cell(row, 7).Value = m.UserName ?? "";
            ws.Cell(row, 8).Value = m.Note ?? "";
            row++;
        }

        ws.Column(1).Style.NumberFormat.Format = DateFormat;

        Finish(ws);
        return Save(workbook);
    }

    private static void WriteHeader(IXLWorksheet ws, IReadOnlyList<LocalizedString> headers)
    {
        for (var i = 0; i < headers.Count; i++)
            ws.Cell(1, i + 1).Value = headers[i].Value;
        var headerRange = ws.Range(1, 1, 1, headers.Count);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
    }

    private static void Finish(IXLWorksheet ws)
    {
        ws.SheetView.FreezeRows(1);
        var used = ws.RangeUsed();
        used?.SetAutoFilter();
        ws.Columns(1, used?.ColumnCount() ?? 1).AdjustToContents();
        foreach (var column in ws.ColumnsUsed())
        {
            if (column.Width > MaxColumnWidth)
                column.Width = MaxColumnWidth;
        }
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
