using Microsoft.AspNetCore.Mvc;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

[Route("Labels")]
public sealed class LabelsController(
    IInventoryService inventory,
    IBarcodeLabelService labels,
    IShopSettingsService settings) : Controller
{
    public const int MaxCopies = 20;
    public const int MaxLabels = 400;

    [HttpGet("")]
    public async Task<IActionResult> Index(TireFilterViewModel filter, int copies = 1)
    {
        copies = Math.Clamp(copies, 1, MaxCopies);
        filter.Page = 1;

        // Bound work before querying: raising copies automatically lowers the
        // number of unique tires so one request never renders thousands of SVGs.
        var maxTires = MaxLabels / copies;
        var result = await inventory.SearchAsync(filter, maxTires);
        var items = result.Items.Select(labels.Create).ToList();

        return View("Sheet", new LabelSheetViewModel
        {
            Labels = Repeat(items, copies),
            Shop = await settings.GetAsync(),
            Filter = filter,
            Copies = copies,
            MatchedTires = result.TotalCount,
            IsTruncated = result.TotalCount > items.Count
        });
    }

    [HttpGet("Tire/{id:int}")]
    public async Task<IActionResult> Tire(int id, int copies = 1)
    {
        var tire = await inventory.GetTireAsync(id);
        if (tire is null) return NotFound();

        copies = Math.Clamp(copies, 1, MaxCopies);
        return View("Sheet", new LabelSheetViewModel
        {
            Labels = Repeat([labels.Create(tire)], copies),
            Shop = await settings.GetAsync(),
            Filter = new TireFilterViewModel(),
            Copies = copies,
            MatchedTires = 1,
            SourceTireId = id
        });
    }

    private static IReadOnlyList<TireLabelViewModel> Repeat(
        IReadOnlyList<TireLabelViewModel> items, int copies)
        => items.SelectMany(item => Enumerable.Repeat(item, copies)).ToList();
}
