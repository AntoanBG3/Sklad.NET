using ClosedXML.Excel;
using Sklad.Models;
using Sklad.Services;

namespace Sklad.Tests;

public class ExcelExportTests
{
    private static readonly ExcelExportService Service = new(new FakeLocalizer<SharedResource>());

    private static Tire NewTire(string sku) => new()
    {
        Sku = sku, Barcode = "3800123456789", Brand = "Michelin", Model = "Primacy 4",
        Width = 205, Profile = 55, Diameter = 16, Season = Season.Summer, Type = TireType.New,
        UnitPrice = 189.99m, Quantity = 4, MinStock = 2, Location = "A1"
    };

    [Fact]
    public void Tires_workbook_has_headers_typed_cells_and_formats()
    {
        var bytes = Service.ExportTires([NewTire("XL-1")]);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        Assert.Equal("SKU", ws.Cell(1, 1).GetString());
        Assert.True(ws.Cell(1, 1).Style.Font.Bold);
        Assert.Equal("XL-1", ws.Cell(2, 1).GetString());
        Assert.Equal("3800123456789", ws.Cell(2, 2).GetString());
        Assert.Equal("205/55 R16", ws.Cell(2, 5).GetString());

        Assert.Equal(XLDataType.Number, ws.Cell(2, 8).DataType);
        Assert.Equal(189.99, ws.Cell(2, 8).GetDouble(), 2);
        Assert.Equal("#,##0.00", ws.Cell(2, 8).Style.NumberFormat.Format);

        // BGN column derives from the fixed rate; stock value = qty * price.
        Assert.Equal(371.59, ws.Cell(2, 9).GetDouble(), 2);
        Assert.Equal(759.96, ws.Cell(2, 12).GetDouble(), 2);

        Assert.Equal(1, ws.SheetView.SplitRow);
        Assert.NotNull(ws.AutoFilter);
    }

    [Fact]
    public void Movements_workbook_writes_shop_local_dates_and_signed_quantities()
    {
        var tire = NewTire("XL-2");
        var utc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var movements = new[]
        {
            new StockMovement { Tire = tire, MovementType = MovementType.In, Quantity = 4, Date = utc, UserName = "maria", Note = "PO-0001" },
            new StockMovement { Tire = tire, MovementType = MovementType.Out, Quantity = 3, Date = utc }
        };

        var bytes = Service.ExportMovements(movements);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();

        Assert.Equal(XLDataType.DateTime, ws.Cell(2, 1).DataType);
        // Sofia is UTC+3 in July.
        Assert.Equal(new DateTime(2026, 7, 1, 15, 0, 0), ws.Cell(2, 1).GetDateTime());
        Assert.Equal(4, ws.Cell(2, 6).GetDouble());
        Assert.Equal(-3, ws.Cell(3, 6).GetDouble());
        Assert.Equal("maria", ws.Cell(2, 7).GetString());
        Assert.Equal("PO-0001", ws.Cell(2, 8).GetString());
    }
}
