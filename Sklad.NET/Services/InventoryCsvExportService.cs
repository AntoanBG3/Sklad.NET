using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using Sklad.Helpers;
using Sklad.Models;

namespace Sklad.Services;

/// <summary>
/// Owns the spreadsheet-facing CSV dialect and localization. Keeping export
/// formatting outside InventoryService leaves the inventory service focused on
/// catalog and ledger behavior.
/// </summary>
public sealed class InventoryCsvExportService : IInventoryCsvExportService
{
    private readonly IStringLocalizer<SharedResource> _localizer;

    public InventoryCsvExportService(IStringLocalizer<SharedResource> localizer)
        => _localizer = localizer;

    public byte[] Export(IEnumerable<Tire> tires)
    {
        var csv = new StringBuilder();
        // European Excel installations often use ';' as the list separator.
        // The directive keeps this comma-delimited file in separate columns.
        csv.AppendLine("sep=,");
        csv.AppendLine(string.Join(',', new[]
        {
            "SKU", "Barcode", "Brand", "Model", "Size", "Season", "Type",
            "Unit Price (EUR)", "Quantity", "Min Stock", "Location"
        }.Select(header => Escape(_localizer[header].Value))));

        foreach (var tire in tires)
        {
            csv.AppendLine(
                $"{Escape(tire.Sku)},{Escape(tire.Barcode ?? "")},{Escape(tire.Brand)},{Escape(tire.Model)},{Escape(tire.Size)}," +
                $"{Escape(_localizer[Enums.Key(tire.Season)].Value)},{Escape(_localizer[Enums.Key(tire.Type)].Value)}," +
                $"{tire.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)},{tire.Quantity},{tire.MinStock},{Escape(tire.Location ?? "")}");
        }

        // Excel otherwise opens a UTF-8 CSV as ANSI and garbles Cyrillic.
        var payload = Encoding.UTF8.GetBytes(csv.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var bytes = new byte[preamble.Length + payload.Length];
        preamble.CopyTo(bytes, 0);
        payload.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static string Escape(string value)
    {
        // Prevent spreadsheet formula injection when an exported value begins
        // with a formula marker.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;

        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
