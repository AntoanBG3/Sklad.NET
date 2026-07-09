# Charted reports and summary indicators

## Context

`TODO.md` lists "charted reports and summary indicators" among its future ideas.
Read literally the idea is already half-delivered: `/Tires/Report` carries a
three-tile stat band (inventory value, SKUs, units) and an animated `share-bar`
next to every brand's share of value. Summary indicators exist.

What does not exist anywhere in the application is **time**. `StockMovement`
records a dated, attributed ledger of every stock change, and no screen has ever
read it as a series. That is the real gap, and it is what this work fills.

Scope, decided during design: **composition charts plus a movement trend**, on
the existing `/Tires/Report` page, drawn with a **vendored Chart.js**, over a
**user-selectable date range**.

The sibling idea, "a mobile interface for working on the warehouse floor", is a
separate project and gets its own spec.

## Facts that shape the work

Each was verified against source, and each contradicts an assumption a reader
might reasonably bring to this feature.

1. **The ledger reconstructs history exactly, but not by summing.** `In` and
   `Out` are deltas; `Adjustment` sets an absolute quantity
   (`InventoryService.RegisterMovementAsync`). Creating a tire with an opening
   quantity writes an initial `Adjustment` (`InventoryService.cs:124`), and a
   tire created at zero writes none, which is consistent because its stock is
   zero. Therefore an `Adjustment` must **never** be summed into a flow chart:
   its `Quantity` is a new absolute level, not an amount moved.

2. **Vendoring a JS library is house style, not a violation of it.**
   `wwwroot/lib` already carries jQuery, jquery-validation and
   jquery-validation-unobtrusive. The "no CDN" rule in `CLAUDE.md` is about
   *remote* assets. Chart.js self-hosted is consistent with existing practice.

3. **There is no Content-Security-Policy header.** `Program.cs:137-139` sets
   `X-Content-Type-Options`, `X-Frame-Options` and `Referrer-Policy` only. An
   inline `<script type="application/json">` data island needs no CSP work.

4. **There is no dark theme.** No `prefers-color-scheme` rule exists in
   `site.css`. Chart colors can be read once from the `:root` custom properties
   and need no theme-change listener.

5. **`Report` has no controller test.** `grep` finds `GetValueReportAsync` only
   in `InventoryServiceTests.cs:502-512`. Nothing asserts the action's model
   type, so introducing a view model breaks no existing test. It also means the
   action is currently unguarded by tests, which this work corrects.

6. **`DateOnly?` query binding is already proven here.** `MovementsController`
   binds `DateOnly? from = null, DateOnly? to = null` straight from the query
   string and hands them to `InventoryService`. Report reuses that exact shape
   rather than inventing a filter view model.

7. **Timestamps are UTC; users think in shop time.** `Dates.Shop` and
   `Dates.StartOfDayUtc` (Europe/Sofia, never the host zone) already exist for
   exactly this. Bucketing must happen in shop time, which SQLite cannot do, so
   it happens in memory.

## Data layer

New types in `Services/InventoryResults.cs`:

```csharp
public enum TrendGranularity { Day, Month }

public record TrendBucket(string Label, DateTime StartUtc, int In, int Out);

public record MovementTrend(
    IReadOnlyList<TrendBucket> Buckets,
    TrendGranularity Granularity,
    int Adjustments);
```

New method on `IInventoryService`:

```csharp
Task<MovementTrend> GetMovementTrendAsync(DateOnly from, DateOnly to);
```

Implementation:

- Convert the inclusive shop-local day range to a half-open UTC interval:
  `Dates.StartOfDayUtc(from)` to `Dates.StartOfDayUtc(to.AddDays(1))`.
- Project only `Date`, `MovementType`, `Quantity` into memory. Volume is a tire
  shop's ledger; this is not a scale concern, and it avoids every SQLite
  translation trap. No decimal is ordered, so the `ORDER BY` decimal gotcha does
  not arise.
- Bucket each row by `Dates.Shop(m.Date)` truncated to day or month.
- `In` sums `MovementType.In` quantities; `Out` sums `MovementType.Out`
  quantities. `Adjustment` rows are excluded from both and counted into
  `Adjustments`.
- Zero-fill: every bucket in the span is emitted, including empty ones, so the
  axis never collapses and a quiet month reads as a quiet month.
- `Label` is formatted server-side under the current culture (`"MMM yyyy"` for
  months, `"d MMM"` for days) so no locale logic reaches JavaScript.

Granularity is a pure, separately testable function:

```csharp
public static TrendGranularity Granularity(DateOnly from, DateOnly to)
    => to.DayNumber - from.DayNumber <= 60 ? TrendGranularity.Day
                                           : TrendGranularity.Month;
```

`GetValueReportAsync` is **unchanged**. The composition charts consume the
`ByBrand` and `BySeason` lists it already returns.

## Range handling

`TiresController.Report` gains optional `DateOnly? from, DateOnly? to`.

- Both absent: `to` is today in shop time, `from` is `today.AddMonths(-11)`.
  Because both endpoints are inclusive, that yields exactly twelve monthly
  buckets; `-12` would render thirteen bars under a "last 12 months" label.
  `Month` granularity suits a strongly seasonal business.
- `from > to`: a localized ModelState error, and the default range is used.
- Span over ten years: a localized ModelState error, and the default range is
  used. This bounds bucket generation; without it, a range of 1900 to 2100 asks
  for 2,400 buckets.
- One bound supplied without the other: the missing bound takes its default.

## View model

```csharp
public class ValueReportViewModel
{
    public required ValueReport Value { get; init; }
    public required MovementTrend Trend { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
}
```

`Views/Tires/Report.cshtml` changes its `@model` from `ValueReport` to this. No
existing test asserts the old type (fact 5).

## Rendering

Chart.js 4 (UMD, no dependencies) vendored at
`wwwroot/lib/chart.js/dist/chart.umd.js`, loaded only from Report's `Scripts`
section so no other page pays for it.

Two canvases:

- **Composition**: a horizontal bar chart of value by brand, and one of value by
  season. Both mirror tables that remain on the page.
- **Trend**: grouped bars, units In against units Out, one group per bucket.

Data crosses to JavaScript through a `<script type="application/json"
id="report-data">` island, parsed by a new `wwwroot/js/report-charts.js`.
Serialize with `System.Text.Json` using the **default** encoder, which escapes
`<` and `>`. This is a security requirement, not a preference: a brand named
`</script>` would otherwise terminate the tag early. Do not substitute
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Non-ASCII escapes to `\uXXXX`,
which is valid JSON and parses back to Cyrillic correctly.

Chart options:

- `animation: false`. Two reasons: `data-print` calls `window.print()`
  immediately, which can capture a canvas mid-tween or before first paint; and
  `site.css` disables animation under `prefers-reduced-motion`, which a canvas
  would otherwise ignore.
- `maintainAspectRatio` set so the canvas does not grow unbounded.
- Colors read once from the `:root` custom properties via `getComputedStyle`,
  so the chart cannot drift from the design system. No theme listener is needed
  (fact 4).

## Accessibility and print

A canvas is opaque to assistive technology. Each canvas takes `role="img"` and a
localized `aria-label` naming what it shows.

The composition charts need no further work: the brand and season tables already
on the page are their accessible equivalent, and they stay. The trend chart has
no such table, so it gains one inside a `<details>` element, which also gives
sighted users the exact numbers behind the bars.

The existing `@media print` block is extended additively. Report and Low Stock
printing must not regress; the print rules for charts are scoped so they cannot
reach other pages, following the `.page:has(.doc)` precedent set by the
purchase-order document.

## Localization

Every new user-facing string gets an entry in `Resources/SharedResource.bg.resx`.
`LocalizationTests.Resx_covers_every_localized_key` now scans Views, Controllers
and Services and fails on any missing key, so an omission turns the suite red
rather than silently rendering English under `bg-BG`.

Bucket labels are produced by `ToString` under the current culture rather than
through the localizer, because month names are culture data, not resource
strings.

## Testing

No migration and no model change, so no schema risk.

Service tests, in `InventoryServiceTests`:

- `Granularity` returns `Day` at a 60-day span and `Month` at 61.
- `In` and `Out` quantities sum into their own series.
- An `Adjustment` is excluded from both series and increments `Adjustments`.
  This is the test that protects fact 1.
- Empty buckets are emitted as zeros across the full span.
- A movement at `22:30 UTC` on 8 July lands in the **9 July** bucket, because
  Sofia is UTC+3 in summer. This is the test that protects fact 7.
- A range whose bounds are equal yields exactly one bucket.

Controller tests, in `ControllerTests`:

- `Report` with no arguments returns a `ValueReportViewModel` spanning the last
  twelve months.
- `Report` with `from > to` adds a ModelState error and falls back to the
  default range.
- `Report` with a span over ten years does the same.

Chart geometry is not unit-tested. It lives in JavaScript, outside xUnit's
reach. This is the accepted cost of the Chart.js decision; the bucketing logic,
which is where the reasoning is, remains fully covered.

## Verification

`dotnet test Sklad.Tests/Sklad.Tests.csproj` green after every commit. CI builds
with `--warnaserror`.

End to end, with `ASPNETCORE_ENVIRONMENT=Development`:

1. Open `/Tires/Report`. Both charts render; the stat band is unchanged.
2. Register an `Out` movement, reload, confirm the trend reflects it.
3. Register an `Adjustment`, reload, confirm the bars do **not** move and the
   corrections count increments.
4. Set a 30-day range and confirm the axis switches to daily buckets.
5. Set `from` after `to` and confirm a localized error, not a crash.
6. Print-preview the page: charts present, no topbar, no footer, no buttons.
7. Switch to `bg-BG` and confirm every label, including month names, is
   Bulgarian.

## Commits

Conventional-commit subjects, each with the suite green:

1. `feat(reports): add movement trend aggregation to InventoryService`
2. `feat(reports): add date range to the stock value report`
3. `chore(assets): vendor Chart.js 4`
4. `feat(reports): chart value composition and movement trend`
5. `docs: record charted reports`

No `Co-Authored-By` trailer.
