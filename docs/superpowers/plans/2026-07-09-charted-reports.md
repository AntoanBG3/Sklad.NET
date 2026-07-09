# Charted Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add composition charts and a date-ranged movement trend chart to `/Tires/Report`, reading the stock ledger as a time series for the first time.

**Architecture:** A pure `Helpers/Trend` class owns bucket arithmetic (granularity, bucket start, sequence, labels) so it is testable without a database. `InventoryService.GetMovementTrendAsync` queries the ledger for a UTC interval, buckets the rows in Europe/Sofia shop time in memory, and zero-fills. `TiresController.Report` binds an optional `DateOnly?` range, validates it, and hands a new `ValueReportViewModel` to the view. The view serializes chart data into a `<script type="application/json">` island; a vendored Chart.js 4 draws it.

**Tech Stack:** ASP.NET Core 10 MVC, EF Core 10 + SQLite, xUnit, Chart.js 4.4.9 (UMD, vendored, no dependencies).

## Global Constraints

- Root namespace is `Sklad`, never `Sklad.NET`. Assembly name differs from root namespace by design.
- Every user-facing string used via `L["..."]` or `_l["..."]` MUST have a `<data name="...">` entry in `Sklad.NET/Resources/SharedResource.bg.resx`, or `LocalizationTests.Resx_covers_every_localized_key` fails. The English string IS the key.
- An `Adjustment` movement's `Quantity` is the **new absolute stock level**, not an amount moved. It MUST NOT be summed into the In or Out series.
- Never `ORDER BY` a decimal column through EF on SQLite. (Not triggered here: trend quantities are `int`.)
- Timestamps are stored UTC and are bucketed in Europe/Sofia shop time via `Helpers.Dates`. Never use the host timezone.
- Serialize the JSON island with `System.Text.Json`'s **default** encoder, which escapes `<` and `>`. Do NOT use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`: a brand named `</script>` would break out of the tag.
- Chart.js MUST be self-hosted under `wwwroot/lib`. No CDN. Ship its LICENSE.md, as every other vendored library does.
- `site.css` keeps a four-colour discipline: `--black`, `--white`, `--gray`, `--red`, with `--ink-70`/`--ink-50`/`--rule-soft` as tints of black (`site.css:18-29`). Red means danger. Charts MUST separate series by tint, never by introducing a colour palette. This also keeps them legible in grayscale print.
- `animation: false` on every chart. `data-print` calls `window.print()` immediately, and `site.css` disables animation under `prefers-reduced-motion`.
- CI builds with `--warnaserror`. Code must be warning-clean.
- Commits carry NO `Co-Authored-By` trailer.
- Do not add comments explaining what code does. Comment only a surprising constraint.

**Test command (every task):** `dotnet test Sklad.Tests/Sklad.Tests.csproj`
**Baseline before starting:** 148 tests passing.

---

### Task 1: Trend bucket arithmetic

**Files:**
- Create: `Sklad.NET/Helpers/Trend.cs`
- Create: `Sklad.Tests/TrendTests.cs`

**Interfaces:**
- Consumes: `TrendGranularity` (defined in this task, in `Trend.cs`).
- Produces: `Sklad.Helpers.Trend.Granularity(DateOnly, DateOnly) -> TrendGranularity`, `Trend.BucketStart(DateOnly, TrendGranularity) -> DateOnly`, `Trend.Sequence(DateOnly, DateOnly, TrendGranularity) -> IEnumerable<DateOnly>`, `Trend.Label(DateOnly, TrendGranularity) -> string`, `enum Sklad.Helpers.TrendGranularity { Day, Month }`.

- [ ] **Step 1: Write the failing tests**

Create `Sklad.Tests/TrendTests.cs`:

```csharp
using System.Globalization;
using Sklad.Helpers;

namespace Sklad.Tests;

public class TrendTests
{
    [Theory]
    [InlineData(60, TrendGranularity.Day)]
    [InlineData(61, TrendGranularity.Month)]
    [InlineData(0, TrendGranularity.Day)]
    [InlineData(365, TrendGranularity.Month)]
    public void Granularity_switches_to_months_past_sixty_days(int spanDays, TrendGranularity expected)
    {
        var from = new DateOnly(2026, 1, 1);
        Assert.Equal(expected, Trend.Granularity(from, from.AddDays(spanDays)));
    }

    [Fact]
    public void BucketStart_truncates_to_the_first_of_the_month()
    {
        Assert.Equal(new DateOnly(2026, 3, 1),
            Trend.BucketStart(new DateOnly(2026, 3, 17), TrendGranularity.Month));
    }

    [Fact]
    public void BucketStart_keeps_the_day_when_daily()
    {
        Assert.Equal(new DateOnly(2026, 3, 17),
            Trend.BucketStart(new DateOnly(2026, 3, 17), TrendGranularity.Day));
    }

    [Fact]
    public void Sequence_covers_every_day_inclusive()
    {
        var days = Trend.Sequence(new DateOnly(2026, 1, 30), new DateOnly(2026, 2, 2), TrendGranularity.Day).ToList();
        Assert.Equal(4, days.Count);
        Assert.Equal(new DateOnly(2026, 1, 30), days[0]);
        Assert.Equal(new DateOnly(2026, 2, 2), days[^1]);
    }

    [Fact]
    public void Sequence_covers_every_month_inclusive_from_partial_months()
    {
        var months = Trend.Sequence(new DateOnly(2026, 1, 17), new DateOnly(2026, 4, 3), TrendGranularity.Month).ToList();
        Assert.Equal(4, months.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), months[0]);
        Assert.Equal(new DateOnly(2026, 4, 1), months[^1]);
    }

    [Fact]
    public void Sequence_of_a_single_day_yields_one_bucket()
    {
        var day = new DateOnly(2026, 5, 5);
        Assert.Single(Trend.Sequence(day, day, TrendGranularity.Day));
    }

    [Fact]
    public void Label_follows_the_current_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            Assert.Equal("Mar 2026", Trend.Label(new DateOnly(2026, 3, 1), TrendGranularity.Month));

            CultureInfo.CurrentCulture = new CultureInfo("bg-BG");
            Assert.DoesNotContain("Mar", Trend.Label(new DateOnly(2026, 3, 1), TrendGranularity.Month));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter TrendTests`
Expected: FAIL to compile, `CS0103: The name 'Trend' does not exist`.

- [ ] **Step 3: Write the implementation**

Create `Sklad.NET/Helpers/Trend.cs`:

```csharp
namespace Sklad.Helpers;

public enum TrendGranularity
{
    Day,
    Month
}

public static class Trend
{
    public const int DailyMaxSpanDays = 60;

    public static TrendGranularity Granularity(DateOnly from, DateOnly to) =>
        to.DayNumber - from.DayNumber <= DailyMaxSpanDays ? TrendGranularity.Day : TrendGranularity.Month;

    public static DateOnly BucketStart(DateOnly day, TrendGranularity granularity) =>
        granularity == TrendGranularity.Day ? day : new DateOnly(day.Year, day.Month, 1);

    public static IEnumerable<DateOnly> Sequence(DateOnly from, DateOnly to, TrendGranularity granularity)
    {
        var cursor = BucketStart(from, granularity);
        var last = BucketStart(to, granularity);
        while (cursor <= last)
        {
            yield return cursor;
            cursor = granularity == TrendGranularity.Day ? cursor.AddDays(1) : cursor.AddMonths(1);
        }
    }

    public static string Label(DateOnly start, TrendGranularity granularity) =>
        granularity == TrendGranularity.Day ? start.ToString("d MMM") : start.ToString("MMM yyyy");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter TrendTests`
Expected: PASS, 10 tests (6 facts plus 4 theory cases).

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj`
Expected: PASS, 158 tests.

- [ ] **Step 6: Commit**

```bash
git add Sklad.NET/Helpers/Trend.cs Sklad.Tests/TrendTests.cs
git commit -m "feat(reports): add trend bucket arithmetic helper"
```

---

### Task 2: Movement trend aggregation

**Files:**
- Modify: `Sklad.NET/Services/InventoryResults.cs` (append records)
- Modify: `Sklad.NET/Services/IInventoryService.cs:32` (add method after `GetValueReportAsync`)
- Modify: `Sklad.NET/Services/InventoryService.cs` (add method after `GetValueReportAsync`, which ends around line 349)
- Test: `Sklad.Tests/InventoryServiceTests.cs` (append)

**Interfaces:**
- Consumes: `Trend.Granularity`, `Trend.BucketStart`, `Trend.Sequence`, `Trend.Label`, `TrendGranularity` from Task 1. `Dates.StartOfDayUtc(DateOnly)` and `Dates.Shop(DateTime)` from `Sklad.Helpers.Dates`.
- Produces: `record TrendBucket(string Label, DateTime StartUtc, int In, int Out)`, `record MovementTrend(IReadOnlyList<TrendBucket> Buckets, TrendGranularity Granularity, int Adjustments)`, and `Task<MovementTrend> IInventoryService.GetMovementTrendAsync(DateOnly from, DateOnly to)`.

- [ ] **Step 1: Write the failing tests**

Append to `Sklad.Tests/InventoryServiceTests.cs`, inside the class:

```csharp
    // --- GetMovementTrendAsync ---

    private async Task SeedMovementAsync(SkladDbContext context, int tireId, MovementType type, int qty, DateTime utc)
    {
        context.StockMovements.Add(new StockMovement
        {
            TireId = tireId, MovementType = type, Quantity = qty, Date = utc
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMovementTrendAsync_sums_in_and_out_into_separate_series()
    {
        await using var context = _db.CreateContext();
        var tire = await SeedTireAsync(NewTire("TREND-1"));
        await SeedMovementAsync(context, tire.Id, MovementType.In, 7, new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc));
        await SeedMovementAsync(context, tire.Id, MovementType.In, 3, new DateTime(2026, 3, 10, 11, 0, 0, DateTimeKind.Utc));
        await SeedMovementAsync(context, tire.Id, MovementType.Out, 4, new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        var trend = await CreateService(context)
            .GetMovementTrendAsync(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 10));

        var bucket = Assert.Single(trend.Buckets);
        Assert.Equal(10, bucket.In);
        Assert.Equal(4, bucket.Out);
    }

    [Fact]
    public async Task GetMovementTrendAsync_excludes_adjustments_from_both_series_and_counts_them()
    {
        await using var context = _db.CreateContext();
        var tire = await SeedTireAsync(NewTire("TREND-2"));
        await SeedMovementAsync(context, tire.Id, MovementType.Adjustment, 500, new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc));
        await SeedMovementAsync(context, tire.Id, MovementType.In, 2, new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc));

        var trend = await CreateService(context)
            .GetMovementTrendAsync(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 10));

        var bucket = Assert.Single(trend.Buckets);
        Assert.Equal(2, bucket.In);
        Assert.Equal(0, bucket.Out);
        Assert.Equal(1, trend.Adjustments);
    }

    [Fact]
    public async Task GetMovementTrendAsync_emits_empty_buckets_as_zeros()
    {
        await using var context = _db.CreateContext();
        var tire = await SeedTireAsync(NewTire("TREND-3"));
        await SeedMovementAsync(context, tire.Id, MovementType.In, 5, new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc));

        var trend = await CreateService(context)
            .GetMovementTrendAsync(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 13));

        Assert.Equal(4, trend.Buckets.Count);
        Assert.Equal(TrendGranularity.Day, trend.Granularity);
        Assert.Equal(0, trend.Buckets[0].In);
        Assert.Equal(5, trend.Buckets[2].In);
    }

    // Sofia is UTC+3 in summer, so 22:30 UTC on 8 July is already 9 July in the shop.
    [Fact]
    public async Task GetMovementTrendAsync_buckets_by_shop_day_not_utc_day()
    {
        await using var context = _db.CreateContext();
        var tire = await SeedTireAsync(NewTire("TREND-4"));
        await SeedMovementAsync(context, tire.Id, MovementType.In, 6, new DateTime(2026, 7, 8, 22, 30, 0, DateTimeKind.Utc));

        var trend = await CreateService(context)
            .GetMovementTrendAsync(new DateOnly(2026, 7, 8), new DateOnly(2026, 7, 9));

        Assert.Equal(0, trend.Buckets[0].In);
        Assert.Equal(6, trend.Buckets[1].In);
    }

    [Fact]
    public async Task GetMovementTrendAsync_groups_by_month_over_a_long_range()
    {
        await using var context = _db.CreateContext();
        var tire = await SeedTireAsync(NewTire("TREND-5"));
        await SeedMovementAsync(context, tire.Id, MovementType.In, 1, new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc));
        await SeedMovementAsync(context, tire.Id, MovementType.In, 2, new DateTime(2026, 1, 25, 9, 0, 0, DateTimeKind.Utc));

        var trend = await CreateService(context)
            .GetMovementTrendAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(TrendGranularity.Month, trend.Granularity);
        Assert.Equal(6, trend.Buckets.Count);
        Assert.Equal(3, trend.Buckets[0].In);
    }

    [Fact]
    public async Task GetMovementTrendAsync_ignores_movements_outside_the_range()
    {
        await using var context = _db.CreateContext();
        var tire = await SeedTireAsync(NewTire("TREND-6"));
        await SeedMovementAsync(context, tire.Id, MovementType.In, 9, new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc));

        var trend = await CreateService(context)
            .GetMovementTrendAsync(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 11));

        Assert.All(trend.Buckets, b => Assert.Equal(0, b.In));
    }
```

Add `using Sklad.Data;` and `using Sklad.Helpers;` to the top of `InventoryServiceTests.cs` if not already present. (`using Sklad.Models;` and `using Sklad.Services;` already are.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter GetMovementTrendAsync`
Expected: FAIL to compile, `CS1061: 'InventoryService' does not contain a definition for 'GetMovementTrendAsync'`.

- [ ] **Step 3: Add the result records**

Append to `Sklad.NET/Services/InventoryResults.cs`:

```csharp
public record TrendBucket(string Label, DateTime StartUtc, int In, int Out);

public record MovementTrend(
    IReadOnlyList<TrendBucket> Buckets,
    Helpers.TrendGranularity Granularity,
    int Adjustments);
```

- [ ] **Step 4: Declare the method on the interface**

In `Sklad.NET/Services/IInventoryService.cs`, immediately after the `GetValueReportAsync` declaration (line 32):

```csharp
    Task<MovementTrend> GetMovementTrendAsync(DateOnly from, DateOnly to);
```

- [ ] **Step 5: Implement it**

In `Sklad.NET/Services/InventoryService.cs`, immediately after the closing brace of `GetValueReportAsync`:

```csharp
    public async Task<MovementTrend> GetMovementTrendAsync(DateOnly from, DateOnly to)
    {
        var fromUtc = Dates.StartOfDayUtc(from);
        var toUtc = Dates.StartOfDayUtc(to.AddDays(1));

        var rows = await _db.StockMovements
            .Where(m => m.Date >= fromUtc && m.Date < toUtc)
            .Select(m => new { m.Date, m.MovementType, m.Quantity })
            .ToListAsync();

        var granularity = Trend.Granularity(from, to);

        var buckets = new List<TrendBucket>();
        var index = new Dictionary<DateOnly, int>();
        foreach (var start in Trend.Sequence(from, to, granularity))
        {
            index[start] = buckets.Count;
            buckets.Add(new TrendBucket(Trend.Label(start, granularity), Dates.StartOfDayUtc(start), 0, 0));
        }

        var adjustments = 0;
        foreach (var row in rows)
        {
            if (row.MovementType == MovementType.Adjustment)
            {
                adjustments++;
                continue;
            }

            var shopDay = DateOnly.FromDateTime(Dates.Shop(row.Date));
            if (!index.TryGetValue(Trend.BucketStart(shopDay, granularity), out var i))
                continue;

            buckets[i] = row.MovementType == MovementType.In
                ? buckets[i] with { In = buckets[i].In + row.Quantity }
                : buckets[i] with { Out = buckets[i].Out + row.Quantity };
        }

        return new MovementTrend(buckets, granularity, adjustments);
    }
```

Ensure `using Sklad.Helpers;` is present at the top of `InventoryService.cs`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter GetMovementTrendAsync`
Expected: PASS, 6 tests.

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj`
Expected: PASS, 164 tests.

- [ ] **Step 8: Commit**

```bash
git add Sklad.NET/Services Sklad.Tests/InventoryServiceTests.cs
git commit -m "feat(reports): add movement trend aggregation to InventoryService"
```

---

### Task 3: Date range on the stock value report

**Files:**
- Create: `Sklad.NET/ViewModels/ValueReportViewModel.cs`
- Modify: `Sklad.NET/Controllers/TiresController.cs:176-181` (the `Report` action)
- Modify: `Sklad.NET/Views/Tires/Report.cshtml:1` (the `@model` line) and add the filter form
- Modify: `Sklad.NET/Resources/SharedResource.bg.resx`
- Test: `Sklad.Tests/ControllerTests.cs` (append to `TiresControllerTests`)

**Interfaces:**
- Consumes: `MovementTrend`, `GetMovementTrendAsync` from Task 2. `ValueReport`, `GetValueReportAsync` (unchanged).
- Produces: `Sklad.ViewModels.ValueReportViewModel` with `ValueReport Value`, `MovementTrend Trend`, `DateOnly From`, `DateOnly To`. `TiresController.Report(DateOnly? from, DateOnly? to)`.

- [ ] **Step 1: Write the failing tests**

Append to `TiresControllerTests` in `Sklad.Tests/ControllerTests.cs`:

```csharp
    [Fact]
    public async Task Report_defaults_to_the_last_twelve_months()
    {
        await using var context = _db.CreateContext();
        var result = Assert.IsType<ViewResult>(await CreateController(context).Report());
        var vm = Assert.IsType<ValueReportViewModel>(result.Model);

        Assert.Equal(vm.To.AddMonths(-11), vm.From);
        Assert.Equal(Sklad.Helpers.TrendGranularity.Month, vm.Trend.Granularity);
        Assert.Equal(12, vm.Trend.Buckets.Count);
    }

    [Fact]
    public async Task Report_rejects_a_start_after_the_end_and_falls_back()
    {
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = Assert.IsType<ViewResult>(
            await controller.Report(new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1)));
        var vm = Assert.IsType<ValueReportViewModel>(result.Model);

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(vm.To.AddMonths(-11), vm.From);
    }

    [Fact]
    public async Task Report_rejects_a_span_over_ten_years_and_falls_back()
    {
        await using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = Assert.IsType<ViewResult>(
            await controller.Report(new DateOnly(2000, 1, 1), new DateOnly(2026, 1, 1)));
        var vm = Assert.IsType<ValueReportViewModel>(result.Model);

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(vm.To.AddMonths(-11), vm.From);
    }

    [Fact]
    public async Task Report_honours_an_explicit_range()
    {
        await using var context = _db.CreateContext();
        var result = Assert.IsType<ViewResult>(
            await CreateController(context).Report(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 20)));
        var vm = Assert.IsType<ValueReportViewModel>(result.Model);

        Assert.Equal(new DateOnly(2026, 3, 1), vm.From);
        Assert.Equal(Sklad.Helpers.TrendGranularity.Day, vm.Trend.Granularity);
        Assert.Equal(20, vm.Trend.Buckets.Count);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter Report_`
Expected: FAIL to compile, `CS0246: The type or namespace name 'ValueReportViewModel' could not be found`.

- [ ] **Step 3: Add the view model**

Create `Sklad.NET/ViewModels/ValueReportViewModel.cs`:

```csharp
using Sklad.Services;

namespace Sklad.ViewModels;

public class ValueReportViewModel
{
    public required ValueReport Value { get; init; }
    public required MovementTrend Trend { get; init; }
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
}
```

- [ ] **Step 4: Rewrite the Report action**

Replace `Sklad.NET/Controllers/TiresController.cs:176-181` with:

```csharp
    private const int MaxSpanDays = 3660;

    // GET: /Tires/Report?from=2026-01-01&to=2026-07-09
    public async Task<IActionResult> Report(DateOnly? from = null, DateOnly? to = null)
    {
        var today = DateOnly.FromDateTime(Dates.Shop(DateTime.UtcNow));
        // Endpoints are inclusive, so -11 yields exactly 12 monthly buckets.
        var defaultFrom = today.AddMonths(-11);

        var start = from ?? defaultFrom;
        var end = to ?? today;

        if (start > end)
        {
            ModelState.AddModelError(string.Empty, _l["The start date cannot be after the end date."]);
            (start, end) = (defaultFrom, today);
        }
        else if (end.DayNumber - start.DayNumber > MaxSpanDays)
        {
            ModelState.AddModelError(string.Empty, _l["Choose a date range of ten years or less."]);
            (start, end) = (defaultFrom, today);
        }

        return View(new ValueReportViewModel
        {
            Value = await _inventory.GetValueReportAsync(),
            Trend = await _inventory.GetMovementTrendAsync(start, end),
            From = start,
            To = end
        });
    }
```

Ensure `using Sklad.Helpers;` and `using Sklad.ViewModels;` are present at the top of `TiresController.cs`.

- [ ] **Step 5: Add the two Bulgarian translations**

In `Sklad.NET/Resources/SharedResource.bg.resx`, before the closing `</root>`:

```xml
  <data name="The start date cannot be after the end date." xml:space="preserve">
    <value>Началната дата не може да бъде след крайната.</value>
  </data>
  <data name="Choose a date range of ten years or less." xml:space="preserve">
    <value>Изберете период от десет години или по-малко.</value>
  </data>
  <data name="Period" xml:space="preserve">
    <value>Период</value>
  </data>
  <data name="From" xml:space="preserve">
    <value>От</value>
  </data>
  <data name="To" xml:space="preserve">
    <value>До</value>
  </data>
  <data name="Apply" xml:space="preserve">
    <value>Приложи</value>
  </data>
  <data name="Corrections in period" xml:space="preserve">
    <value>Корекции за периода</value>
  </data>
  <data name="Movement Activity" xml:space="preserve">
    <value>Движение на склада</value>
  </data>
  <data name="Units received and shipped over the selected period. Adjustments are corrections to absolute stock and are excluded." xml:space="preserve">
    <value>Получени и изведени бройки за избрания период. Корекциите задават абсолютна наличност и не се включват.</value>
  </data>
  <data name="Value by Brand" xml:space="preserve">
    <value>Стойност по марка</value>
  </data>
  <data name="Value by Season" xml:space="preserve">
    <value>Стойност по сезон</value>
  </data>
  <data name="Show the numbers" xml:space="preserve">
    <value>Покажи числата</value>
  </data>
```

Some of these keys are consumed in Task 4; adding them now keeps `Resx_covers_every_localized_key` green in both tasks. Before adding, check each key is not already present: `grep -c 'data name="From"' Sklad.NET/Resources/SharedResource.bg.resx`. Skip any that already exist — a duplicate `data name` makes the resx fail to compile.

- [ ] **Step 6: Update the view's model directive and add the filter form**

Change `Sklad.NET/Views/Tires/Report.cshtml:1` from `@model Sklad.Services.ValueReport` to:

```cshtml
@model Sklad.ViewModels.ValueReportViewModel
```

Then replace every bare `Model.` reference to report data with `Model.Value.`:
- `Model.ByBrand` becomes `Model.Value.ByBrand` (4 occurrences: lines 29, 55, 75, and the season block's sibling)
- `Model.BySeason` becomes `Model.Value.BySeason` (2 occurrences)
- `Model.TotalValue` becomes `Model.Value.TotalValue` (3 occurrences)

Verify none remain: `grep -n 'Model\.\(ByBrand\|BySeason\|TotalValue\)' Sklad.NET/Views/Tires/Report.cshtml` must print nothing.

Add the range form immediately after the closing `</div>` of `.statband`:

```cshtml
<form method="get" class="block filter-inline">
    <div asp-validation-summary="ModelOnly" class="form-errors"></div>
    <div class="form-grid-3">
        <div class="field">
            <label class="field-label" for="from">@L["From"]</label>
            <input class="field-input" type="date" id="from" name="from" value="@Model.From.ToString("yyyy-MM-dd")" />
        </div>
        <div class="field">
            <label class="field-label" for="to">@L["To"]</label>
            <input class="field-input" type="date" id="to" name="to" value="@Model.To.ToString("yyyy-MM-dd")" />
        </div>
        <div class="field">
            <span class="field-label">&nbsp;</span>
            <button type="submit" class="btn btn-primary">@L["Apply"]</button>
        </div>
    </div>
</form>
```

`type="date"` always submits ISO `yyyy-MM-dd`, which `DateOnly` model binding accepts regardless of the active culture. This mirrors `Views/Movements/Index.cshtml`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj`
Expected: PASS, 168 tests. `Resx_covers_every_localized_key` must be among them and green.

- [ ] **Step 8: Build warning-clean**

Run: `dotnet build Sklad.NET/Sklad.NET.csproj --warnaserror`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 9: Commit**

```bash
git add Sklad.NET/ViewModels/ValueReportViewModel.cs Sklad.NET/Controllers/TiresController.cs Sklad.NET/Views/Tires/Report.cshtml Sklad.NET/Resources/SharedResource.bg.resx Sklad.Tests/ControllerTests.cs
git commit -m "feat(reports): add a date range to the stock value report"
```

---

### Task 4: Vendor Chart.js

**Files:**
- Create: `Sklad.NET/wwwroot/lib/chart.js/dist/chart.umd.js`
- Create: `Sklad.NET/wwwroot/lib/chart.js/LICENSE.md`
- Create: `Sklad.Tests/TestPaths.cs`
- Modify: `Sklad.Tests/LocalizationTests.cs` (drop its private `RepoRoot`, use `TestPaths.RepoRoot`)
- Test: `Sklad.Tests/SmokeTests.cs` (append)

**Interfaces:**
- Consumes: nothing.
- Produces: the global `Chart` constructor at `~/lib/chart.js/dist/chart.umd.js`, consumed by Task 5. `Sklad.Tests.TestPaths.RepoRoot() -> string` and `TestPaths.App() -> string`.

Vendoring gets its own commit because it is a licence-bearing third-party asset, and a reviewer should be able to approve or reject it without also reviewing chart code.

- [ ] **Step 1: Extract the shared path helper**

`LocalizationTests.cs:103` already has a `private static RepoRoot([CallerFilePath])`. Rather than duplicate it, lift it into a shared helper. `[CallerFilePath]` resolves at compile time to the source file of the **call site**, so the helper must not compute the root from its own path — it takes the caller's path as a defaulted parameter exactly as before, and every test file in `Sklad.Tests/` sits one level below the repo root.

Create `Sklad.Tests/TestPaths.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace Sklad.Tests;

internal static class TestPaths
{
    public static string RepoRoot([CallerFilePath] string callerPath = "") =>
        Directory.GetParent(Path.GetDirectoryName(callerPath)!)!.FullName;

    public static string App([CallerFilePath] string callerPath = "") =>
        Path.Combine(RepoRoot(callerPath), "Sklad.NET");
}
```

In `Sklad.Tests/LocalizationTests.cs`, delete the private `RepoRoot` method and replace the single call `RepoRoot()` in `Resx_covers_every_localized_key` with `TestPaths.RepoRoot()`. Remove the now-unused `using System.Runtime.CompilerServices;` if nothing else needs it, or `--warnaserror` will not complain but the reviewer will.

Run `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter LocalizationTests` and expect all 3 to still pass. This is a pure refactor; if `Resx_covers_every_localized_key` starts reporting `Only N localized keys found`, the path resolution broke and `TestPaths.RepoRoot` is computing from the wrong file.

- [ ] **Step 2: Write the failing test**

Append to `Sklad.Tests/SmokeTests.cs`:

```csharp
    [Fact]
    public void Chart_js_is_vendored_and_tracked()
    {
        var lib = Path.Combine(TestPaths.App(), "wwwroot", "lib", "chart.js");
        Assert.True(File.Exists(Path.Combine(lib, "dist", "chart.umd.js")),
            "Chart.js must be self-hosted; the report page loads it from wwwroot.");
        Assert.True(File.Exists(Path.Combine(lib, "LICENSE.md")),
            "Every vendored library in wwwroot/lib ships its licence.");
    }
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter Chart_js_is_vendored_and_tracked`
Expected: FAIL, "Chart.js must be self-hosted".

- [ ] **Step 4: Download the library**

```bash
mkdir -p Sklad.NET/wwwroot/lib/chart.js/dist
curl -sSL -o Sklad.NET/wwwroot/lib/chart.js/dist/chart.umd.js https://cdn.jsdelivr.net/npm/chart.js@4.4.9/dist/chart.umd.js
curl -sSL -o Sklad.NET/wwwroot/lib/chart.js/LICENSE.md https://raw.githubusercontent.com/chartjs/Chart.js/v4.4.9/LICENSE.md
```

Verify the download is the real thing, not an error page:

```bash
wc -c Sklad.NET/wwwroot/lib/chart.js/dist/chart.umd.js
head -c 80 Sklad.NET/wwwroot/lib/chart.js/LICENSE.md
```

Expected: roughly 206,670 bytes, and the licence begins with the MIT header. If the byte count is under 100,000, the download failed; do not commit it.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter Chart_js_is_vendored_and_tracked`
Expected: PASS.

- [ ] **Step 6: Confirm git does not ignore it**

Run: `git check-ignore -v Sklad.NET/wwwroot/lib/chart.js/dist/chart.umd.js; echo "exit=$?"`
Expected: no output and `exit=1`, meaning the file is not ignored.

- [ ] **Step 7: Commit**

```bash
git add Sklad.NET/wwwroot/lib/chart.js Sklad.Tests/SmokeTests.cs Sklad.Tests/TestPaths.cs Sklad.Tests/LocalizationTests.cs
git commit -m "chore(assets): vendor Chart.js 4.4.9"
```

---

### Task 5: Draw the charts

**Files:**
- Create: `Sklad.NET/wwwroot/js/report-charts.js`
- Modify: `Sklad.NET/Views/Tires/Report.cshtml` (data island, canvases, trend table, Scripts section)
- Modify: `Sklad.NET/wwwroot/css/site.css` (append a `.chart` block and extend the print rules)

**Interfaces:**
- Consumes: `ValueReportViewModel` from Task 3, the global `Chart` from Task 4.
- Produces: nothing consumed by later tasks.

Chart geometry is not unit-tested; it lives in JavaScript, outside xUnit's reach. This is the accepted cost of the Chart.js decision recorded in the spec. The suite must stay green, and verification here is the manual checklist in Step 7.

- [ ] **Step 1: Add the data island and canvases to the view**

In `Sklad.NET/Views/Tires/Report.cshtml`, add to the `@{ }` block at the top:

```cshtml
@using System.Text.Json
@{
    ViewData["Title"] = L["Stock Value"].Value;

    // Default JsonSerializer encoder escapes < and >, so a brand named
    // "</script>" cannot terminate the data island early. Never relax it.
    var chartJson = JsonSerializer.Serialize(new
    {
        brandLabels = Model.Value.ByBrand.Select(g => g.Key),
        brandValues = Model.Value.ByBrand.Select(g => g.Value),
        seasonLabels = Model.Value.BySeason.Select(g => L[g.Key].Value),
        seasonValues = Model.Value.BySeason.Select(g => g.Value),
        trendLabels = Model.Trend.Buckets.Select(b => b.Label),
        trendIn = Model.Trend.Buckets.Select(b => b.In),
        trendOut = Model.Trend.Buckets.Select(b => b.Out),
        inLabel = L["In"].Value,
        outLabel = L["Out"].Value
    });
}
```

Insert the trend section immediately before the existing `<div class="grid">`:

```cshtml
<div class="block">
    <div class="block-head">
        <h2 class="block-title">@L["Movement Activity"]</h2>
        <span class="block-title-context">@L["Corrections in period"]: @Model.Trend.Adjustments</span>
    </div>
    <p class="page-lede">@L["Units received and shipped over the selected period. Adjustments are corrections to absolute stock and are excluded."]</p>
    <div class="chart chart-trend">
        <canvas id="trend-chart" role="img" aria-label="@L["Movement Activity"]"></canvas>
    </div>
    <details class="chart-data">
        <summary>@L["Show the numbers"]</summary>
        <div class="table-scroll" tabindex="0">
            <table class="data-table">
                <thead>
                    <tr>
                        <th scope="col">@L["Period"]</th>
                        <th scope="col" class="col-num">@L["In"]</th>
                        <th scope="col" class="col-num">@L["Out"]</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var b in Model.Trend.Buckets)
                    {
                        <tr>
                            <td>@b.Label</td>
                            <td class="col-num">@b.In</td>
                            <td class="col-num">@b.Out</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </details>
</div>
```

Inside the existing `By Brand` block, immediately above its `<div class="table-scroll">`, add:

```cshtml
<div class="chart"><canvas id="brand-chart" role="img" aria-label="@L["Value by Brand"]"></canvas></div>
```

And inside the `By Season` block, in the same position:

```cshtml
<div class="chart"><canvas id="season-chart" role="img" aria-label="@L["Value by Season"]"></canvas></div>
```

At the very end of the file:

```cshtml
<script type="application/json" id="report-data">@Html.Raw(chartJson)</script>

@section Scripts {
    <script src="~/lib/chart.js/dist/chart.umd.js"></script>
    <script src="~/js/report-charts.js" asp-append-version="true"></script>
}
```

- [ ] **Step 2: Write the chart script**

Create `Sklad.NET/wwwroot/js/report-charts.js`:

```javascript
(function () {
    var island = document.getElementById('report-data');
    if (!island || typeof Chart === 'undefined') return;

    var data = JSON.parse(island.textContent);
    var css = getComputedStyle(document.documentElement);
    var read = function (name, fallback) {
        return css.getPropertyValue(name).trim() || fallback;
    };

    var black = read('--black', '#000000');
    var ruleSoft = read('--rule-soft', 'rgba(0,0,0,.14)');
    var ink70 = read('--ink-70', 'rgba(0,0,0,.70)');
    var ink50 = read('--ink-50', 'rgba(0,0,0,.58)');

    // site.css keeps a four-colour discipline (black, white, gray, red), and red
    // is reserved for danger. Charts therefore separate series by tint of black,
    // which also survives a grayscale print rather than collapsing to one shade.
    var inFill = black;
    var outFill = 'rgba(0, 0, 0, .32)';

    // window.print() fires immediately from the Print button and would otherwise
    // capture a canvas mid-tween; this also honours prefers-reduced-motion,
    // which CSS cannot enforce on a canvas.
    Chart.defaults.animation = false;
    Chart.defaults.color = ink50;
    Chart.defaults.borderColor = ruleSoft;
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;

    function horizontalBar(id, labels, values) {
        var el = document.getElementById(id);
        if (!el || !labels.length) return;
        new Chart(el, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{ data: values, backgroundColor: ink70, hoverBackgroundColor: black, borderWidth: 0 }]
            },
            options: {
                indexAxis: 'y',
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: { x: { beginAtZero: true, grid: { color: ruleSoft } }, y: { grid: { display: false } } }
            }
        });
    }

    horizontalBar('brand-chart', data.brandLabels, data.brandValues);
    horizontalBar('season-chart', data.seasonLabels, data.seasonValues);

    var trendEl = document.getElementById('trend-chart');
    if (trendEl) {
        new Chart(trendEl, {
            type: 'bar',
            data: {
                labels: data.trendLabels,
                datasets: [
                    { label: data.inLabel, data: data.trendIn, backgroundColor: inFill, borderWidth: 0 },
                    { label: data.outLabel, data: data.trendOut, backgroundColor: outFill, borderWidth: 0 }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'top', align: 'end' } },
                scales: {
                    x: { grid: { display: false } },
                    y: { beginAtZero: true, ticks: { precision: 0 }, grid: { color: ruleSoft } }
                }
            }
        });
    }
})();
```

The token names above were verified against `site.css:18-29`: `--black`, `--ink-70`, `--ink-50` and `--rule-soft` all exist. There is no `--ink` token, and `--rule` is pure `#000000`, which is why gridlines use `--rule-soft` instead. The font comes from the computed body style rather than a token, because none exists.

- [ ] **Step 3: Add the styles**

Append to `Sklad.NET/wwwroot/css/site.css`, before the `@media print` blocks:

```css
/* ============================================================
   Charts
   ============================================================ */
.chart { position: relative; height: 15rem; padding: var(--sp-4); }
.chart-trend { height: 20rem; }
.chart-data { border-top: 1px solid var(--rule-soft); }
.chart-data summary {
    padding: var(--sp-3) var(--sp-4);
    cursor: pointer;
    font-size: var(--f-0);
    color: var(--ink-50);
}
```

Then append a print rule. Do not touch the existing selectors inside the print blocks; scope the new ones so Low Stock and the purchase-order document cannot be affected:

```css
@media print {
    .chart { height: 12rem; page-break-inside: avoid; }
    .chart-data { display: none; }
}
```

The `<details>` table is hidden in print because the chart itself carries the numbers visually and the table would double the page count.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj`
Expected: PASS, 169 tests. No new tests here; this step proves no regression, especially `Resx_covers_every_localized_key`.

- [ ] **Step 5: Build warning-clean**

Run: `dotnet build Sklad.NET/Sklad.NET.csproj --warnaserror`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Verify the tag-helper output is not double-encoded**

The data island must contain raw JSON. Run the app and fetch the page (see Step 7 for the sign-in dance), then:

```bash
grep -o 'id="report-data">.\{0,60\}' /tmp/report.html
```

Expected: `id="report-data">{"brandLabels":[...` and NOT `{&quot;brandLabels&quot;`. If it is HTML-encoded, `@Html.Raw` is missing.

- [ ] **Step 7: Manual verification against the running app**

```bash
export ASPNETCORE_ENVIRONMENT=Development
cd Sklad.NET && dotnet run --no-launch-profile --urls http://localhost:5246
```

Sign in at `http://localhost:5246` as `admin` / `sklad-dev`, then confirm each:

1. `/Tires/Report` renders three charts and the stat band is unchanged.
2. Register an `Out` movement on any tire, reload the report, the Out bar grows.
3. Register an `Adjustment`, reload: **neither bar moves** and "Corrections in period" increments by one. This is the behaviour the whole design turns on.
4. Set the range to the last 30 days and confirm the trend axis switches to daily labels.
5. Set `from` after `to` and confirm a localized error appears above the form and the page still renders over the default range.
6. Ctrl+P: charts are present, the topbar, footer, buttons and the `Show the numbers` table are gone.
7. Switch to Bulgarian and confirm every label, including the month names on the axis, is Bulgarian.
8. Expand `Show the numbers` and confirm the table's In/Out columns match the bars.

- [ ] **Step 8: Commit**

```bash
git add Sklad.NET/Views/Tires/Report.cshtml Sklad.NET/wwwroot/js/report-charts.js Sklad.NET/wwwroot/css/site.css
git commit -m "feat(reports): chart value composition and movement trend"
```

---

### Task 6: Documentation

**Files:**
- Modify: `TODO.md`
- Modify: `CLAUDE.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Record the work in TODO.md**

Add after item 10, and remove `- Charted reports and summary indicators` from the Future ideas list, leaving four.

```markdown
## [x] 11. Charted reports over a date range (2026-07-09)

`/Tires/Report` now charts what the warehouse holds and how it moves. Value by
brand and by season are drawn beside the tables they summarise, and a new
movement trend charts units received against units shipped over a
user-selectable range, bucketed by day up to 60 days and by month beyond.

Adjustments are excluded from the trend and counted separately: an adjustment
stores the new absolute stock level, not an amount moved, so summing it into a
flow would be meaningless. Buckets are cut on Europe/Sofia calendar days, not
UTC ones, so a movement recorded at 22:30 UTC belongs to the next shop day.

Chart.js 4.4.9 is vendored into `wwwroot/lib` beside jQuery. Chart data crosses
to JavaScript through a JSON island serialized with the default encoder, which
escapes `<` so a brand name cannot break out of the script tag. Animation is
off so printing cannot catch a canvas mid-tween.

No migration; tests 148 → 169.
```

- [ ] **Step 2: Update CLAUDE.md**

Three edits:

1. In the opening paragraph, `xUnit test suite in \`Sklad.Tests\` (148 tests)` becomes `(169 tests)`.
2. In the `Controllers/` block, the `TiresController.cs` line gains `Report` taking a date range: change `Report (value)` to `Report (value + charted movement trend, date-ranged)`.
3. Add to `## Gotchas`:

```markdown
- The movement trend excludes `Adjustment` movements from its In/Out bars and counts them separately: an adjustment's `Quantity` is the **new absolute stock level**, not an amount moved, so summing it into a flow is nonsense. Buckets are cut on Europe/Sofia calendar days via `Dates.Shop`, never UTC days.
- Chart data reaches JavaScript through a `<script type="application/json">` island serialized by `System.Text.Json` with its **default** encoder, which escapes `<` and `>`. Never switch it to `UnsafeRelaxedJsonEscaping`: a brand named `</script>` would break out of the tag. Chart.js runs with `animation: false` because `data-print` calls `window.print()` immediately and a canvas ignores `prefers-reduced-motion`.
```

4. Add `Chart.js 4 (vendored, `wwwroot/lib/chart.js`)` to the `wwwroot/` entry in the project layout.

- [ ] **Step 3: Update README.md**

Change the test count from 148 to 169, and add charted reports to the feature list in the same voice as the surrounding prose.

- [ ] **Step 4: Verify the suite and the tree**

```bash
dotnet test Sklad.Tests/Sklad.Tests.csproj
git status --short
```

Expected: 169 passing; only the three doc files modified.

- [ ] **Step 5: Commit**

```bash
git add TODO.md CLAUDE.md README.md
git commit -m "docs: record charted reports over a date range"
```

---

## Self-review notes

Spec coverage checked section by section. Data layer maps to Tasks 1 and 2; range handling and the view model to Task 3; rendering to Tasks 4 and 5; accessibility and print to Task 5 Step 1 and Step 3; localization to Task 3 Step 5, enforced continuously by the existing coverage test; testing to the named tests in Tasks 1, 2 and 3; verification to Task 5 Step 7.

Type consistency checked. `TrendGranularity` is declared once in `Helpers/Trend.cs` and referenced as `Helpers.TrendGranularity` from `InventoryResults.cs` and as `Sklad.Helpers.TrendGranularity` from tests. `TrendBucket` uses `In`/`Out` in Tasks 2, 3 and 5. `ValueReportViewModel` exposes `Value`/`Trend`/`From`/`To` in Tasks 3 and 5 alike.

Self-review caught three defects, fixed inline. Test-count arithmetic was wrong from Task 4 onward: vendoring adds a smoke test, so the final total is 169, not 168. Task 3 Step 1 carried a mangled test body with a nonsense assertion followed by a correction; the broken version is gone. And the chart palette originally introduced six colours, which would have broken the four-colour discipline `site.css` holds to deliberately; series now separate by tint of black, which is both in-system and legible in grayscale print.

One carried risk, stated rather than hidden: chart geometry has no automated coverage. That is the accepted cost of choosing Chart.js over inline SVG, recorded in the spec. Task 5 Step 7 is the manual checklist that stands in for it, and its item 3 (an `Adjustment` must move neither bar) is the one that would catch the design's central failure mode.
