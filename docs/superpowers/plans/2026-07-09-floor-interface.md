# Warehouse Floor Interface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A focused scan-and-book flow at `/Floor` so a picker holding a phone books a stock movement in one screen instead of five page loads.

**Architecture:** A new `FloorController` with its own slim layout, selected by a `Views/Floor/_ViewStart.cshtml`. It orchestrates only: code lookup goes through the existing `IInventoryService.FindByCodeAsync`, and booking goes through the existing `RegisterMovementAsync`, which already guards stock, attributes the user, retries on concurrency conflict, and returns the new quantity. No new stock logic exists anywhere in this feature.

**Tech Stack:** ASP.NET Core 10 MVC, Razor, the project's hand-rolled CSS design system, xUnit. No new packages.

## Global Constraints

- Root namespace is `Sklad`, never `Sklad.NET`.
- **`Adjustment` must never be bookable from the floor.** `MovementType` binds from the form; `Adjustment` sets stock *absolutely* and `RegisterMovementAsync` permits a quantity of zero for it. A crafted `POST /Floor/Book` with `MovementType=Adjustment&Quantity=0` would zero a tire. Omitting the option from the view is NOT a control. The controller rejects anything that is not `In` or `Out`, and a test asserts on the database.
- `Tire.Quantity` changes **only** through `RegisterMovementAsync`. This controller must never touch `SkladDbContext` or write a quantity. Controllers orchestrate; services hold the rules.
- Every user-facing string via `L["..."]` or `_l["..."]` MUST have a `<data name="...">` entry in `Sklad.NET/Resources/SharedResource.bg.resx`, or `LocalizationTests.Resx_covers_every_localized_key` fails. The English string IS the key. Check a key does not already exist before adding it: a duplicate `<data name>` breaks the .resx compile.
- `site.css` keeps a four-colour discipline: `--black`, `--white`, `--gray`, `--red`, with `--ink-70`/`--ink-50`/`--rule-soft` as tints of black. **Red means danger** — use it for the insufficient-stock message, NOT for the Out button. Out is a tint.
- The booking screen must work without JavaScript. The stepper is a real `<input type="number">`; the `−`/`+` buttons are progressive enhancement.
- `/Tires/Scan`, `_Layout.cshtml` and every existing view are **unchanged**. This feature adds files. The only shared file it modifies is `site.css` (append only) and the .resx.
- CI builds with `--warnaserror`. Code must be warning-clean.
- Do not add comments explaining what code does. Comment only a surprising constraint.
- Commits carry NO `Co-Authored-By` trailer.

**Test command (every task):** `dotnet test Sklad.Tests/Sklad.Tests.csproj`
**Baseline before starting:** 171 tests passing, on `master`.

**Existing API you consume (do not re-implement):**
```csharp
Task<Tire?> IInventoryService.FindByCodeAsync(string code);   // matches Sku OR Barcode, NOCASE collation
Task<Tire?> IInventoryService.GetTireAsync(int id, bool includeMovements = false);
Task<int>   IInventoryService.RegisterMovementAsync(int tireId, MovementType movementType,
                                                    int quantity, string? note, string? userName = null);
                                                    // returns the NEW quantity
```
Exceptions it throws: `InsufficientStockException` (has `.Available`, `.Requested`), `InvalidMovementQuantityException`, `TireNotFoundException`.
`Tire.Size` is a `[NotMapped]` computed string. `TempData["Flash"]` is the flash convention, rendered by the layout.

---

### Task 1: The floor area — slim layout, scan screen, booking screen

**Files:**
- Create: `Sklad.NET/Controllers/FloorController.cs`
- Create: `Sklad.NET/ViewModels/FloorScanViewModel.cs`
- Create: `Sklad.NET/ViewModels/FloorBookViewModel.cs`
- Create: `Sklad.NET/Views/Shared/_FloorLayout.cshtml`
- Create: `Sklad.NET/Views/Floor/_ViewStart.cshtml`
- Create: `Sklad.NET/Views/Floor/Index.cshtml`
- Create: `Sklad.NET/Views/Floor/Tire.cshtml`
- Create: `Sklad.NET/wwwroot/js/floor.js`
- Modify: `Sklad.NET/wwwroot/css/site.css` (append a Floor block **before** the first `@media print` block; modify no existing selector)
- Modify: `Sklad.NET/Resources/SharedResource.bg.resx`
- Create: `Sklad.Tests/FloorControllerTests.cs`

**Interfaces:**
- Consumes: `IInventoryService.FindByCodeAsync` (unchanged).
- Produces: `FloorController.Index()`, `FloorController.Tire(string? code)`, `Sklad.ViewModels.FloorScanViewModel { string? Code; bool NotFound; }`, `Sklad.ViewModels.FloorBookViewModel` with `int TireId`, `string Sku`, `string Description`, `string Size`, `string? Location`, `int Quantity`, and `static FloorBookViewModel FromTire(Tire)`.

This task deliberately ships **no POST**. The booking screen renders and its buttons submit to an action that does not exist yet, which Task 2 adds. That is reviewable on its own: the layout, the lookup and the screen are one coherent unit, and no stock can be written.

- [ ] **Step 1: Write the failing tests**

Create `Sklad.Tests/FloorControllerTests.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Sklad.Controllers;
using Sklad.Data;
using Sklad.Models;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Tests;

public class FloorControllerTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    private FloorController CreateController(SkladDbContext context, string userName = "picker")
    {
        var service = new InventoryService(context, NullLogger<InventoryService>.Instance, new FakeLocalizer<SharedResource>());
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, userName) }, "Test"))
        };
        return new FloorController(service, new FakeLocalizer<SharedResource>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NullTempDataProvider())
        };
    }

    private static Tire NewTire(string sku, int qty = 10, string? barcode = null) => new()
    {
        Sku = sku, Brand = "Michelin", Model = "Primacy 4", Width = 205, Profile = 55, Diameter = 16,
        Season = Season.Summer, Type = TireType.New, UnitPrice = 100m, Quantity = qty, MinStock = 2,
        Barcode = barcode, Location = "A-12"
    };

    private async Task<Tire> SeedAsync(Tire tire)
    {
        await using var context = _db.CreateContext();
        context.Tires.Add(tire);
        await context.SaveChangesAsync();
        return tire;
    }

    [Fact]
    public void Index_renders_the_scan_screen()
    {
        using var context = _db.CreateContext();
        var result = Assert.IsType<ViewResult>(CreateController(context).Index());
        var vm = Assert.IsType<FloorScanViewModel>(result.Model);
        Assert.False(vm.NotFound);
    }

    [Fact]
    public async Task Tire_with_a_blank_code_returns_to_the_scan_screen()
    {
        using var context = _db.CreateContext();
        var result = Assert.IsType<RedirectToActionResult>(await CreateController(context).Tire("   "));
        Assert.Equal(nameof(FloorController.Index), result.ActionName);
    }

    [Fact]
    public async Task Tire_with_an_unknown_code_re_renders_the_scan_screen()
    {
        using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await CreateController(context).Tire("NOPE-1"));

        Assert.Equal(nameof(FloorController.Index), result.ViewName);
        var vm = Assert.IsType<FloorScanViewModel>(result.Model);
        Assert.True(vm.NotFound);
        Assert.Equal("NOPE-1", vm.Code);
    }

    [Fact]
    public async Task Tire_finds_a_tire_by_sku()
    {
        var tire = await SeedAsync(NewTire("MI-205", qty: 7));
        using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await CreateController(context).Tire("MI-205"));
        var vm = Assert.IsType<FloorBookViewModel>(result.Model);

        Assert.Equal(tire.Id, vm.TireId);
        Assert.Equal(7, vm.Quantity);
        Assert.Equal("A-12", vm.Location);
    }

    // Sku and Barcode carry a NOCASE collation; this proves the lookup relies on it.
    [Fact]
    public async Task Tire_finds_a_tire_by_barcode_in_a_different_case()
    {
        await SeedAsync(NewTire("MI-206", barcode: "ABC123"));
        using var context = _db.CreateContext();

        var result = Assert.IsType<ViewResult>(await CreateController(context).Tire("abc123"));
        var vm = Assert.IsType<FloorBookViewModel>(result.Model);

        Assert.Equal("MI-206", vm.Sku);
    }
}
```

`NullTempDataProvider` and `FakeLocalizer<T>` already exist in `Sklad.Tests` (see `ControllerTests.cs`). Do not redefine them. Note the controller constructor takes `(IInventoryService, IStringLocalizer<SharedResource>)` — the localizer is unused until Task 2, so if `--warnaserror` rejects the unused `_l` field, keep the constructor parameter and the field but confirm the build; C# does not warn on an unused `private readonly` reference-type field by default. If it does warn, say so in your report rather than deleting the parameter, because Task 2 needs it.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter FloorControllerTests`
Expected: FAIL to compile, `CS0246: The type or namespace name 'FloorController' could not be found`.

- [ ] **Step 3: Add the view models**

Create `Sklad.NET/ViewModels/FloorScanViewModel.cs`:

```csharp
namespace Sklad.ViewModels;

public class FloorScanViewModel
{
    public string? Code { get; set; }
    public bool NotFound { get; set; }
}
```

Create `Sklad.NET/ViewModels/FloorBookViewModel.cs`:

```csharp
using Sklad.Models;

namespace Sklad.ViewModels;

public class FloorBookViewModel
{
    public required int TireId { get; init; }
    public required string Sku { get; init; }
    public required string Description { get; init; }
    public required string Size { get; init; }
    public string? Location { get; init; }
    public required int Quantity { get; init; }

    public static FloorBookViewModel FromTire(Tire tire) => new()
    {
        TireId = tire.Id,
        Sku = tire.Sku,
        Description = $"{tire.Brand} {tire.Model}",
        Size = tire.Size,
        Location = tire.Location,
        Quantity = tire.Quantity
    };
}
```

- [ ] **Step 4: Add the controller**

Create `Sklad.NET/Controllers/FloorController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Sklad.Services;
using Sklad.ViewModels;

namespace Sklad.Controllers;

public class FloorController : Controller
{
    private readonly IInventoryService _inventory;
    private readonly IStringLocalizer<SharedResource> _l;

    public FloorController(IInventoryService inventory, IStringLocalizer<SharedResource> l)
    {
        _inventory = inventory;
        _l = l;
    }

    private string? CurrentUser => User.Identity?.Name;

    // GET: /Floor
    public IActionResult Index() => View(new FloorScanViewModel());

    // GET: /Floor/Tire?code=...
    public async Task<IActionResult> Tire(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return RedirectToAction(nameof(Index));

        var trimmed = code.Trim();
        var tire = await _inventory.FindByCodeAsync(trimmed);
        if (tire is null)
            return View(nameof(Index), new FloorScanViewModel { Code = trimmed, NotFound = true });

        return View(FloorBookViewModel.FromTire(tire));
    }
}
```

`CurrentUser` is unused until Task 2 and mirrors `TiresController.cs:24`. If the build warns about it, note that in your report; do not delete it.

- [ ] **Step 5: Add the layout and view-start**

Create `Sklad.NET/Views/Floor/_ViewStart.cshtml`:

```cshtml
@{
    Layout = "_FloorLayout";
}
```

Create `Sklad.NET/Views/Shared/_FloorLayout.cshtml`. Model it on `_Layout.cshtml` (its head block and its `TempData["Flash"]` region), but with no topbar, no nav, no footer:

```cshtml
@{
    var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
}
<!DOCTYPE html>
<html lang="@lang">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] &mdash; Sklad</title>
    <link rel="icon" type="image/svg+xml" href="~/favicon.svg" asp-append-version="true" />
    <link rel="preload" href="~/fonts/InterVariable.woff2" as="font" type="font/woff2" crossorigin />
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true" />
</head>
<body>
    <header class="floor-bar">
        <span class="wordmark-sklad">Sklad</span>
        <a class="floor-exit" asp-controller="Tires" asp-action="Index">@L["Full app"]</a>
    </header>

    @if (TempData["Flash"] is string flash)
    {
        <div class="flash" role="status"><div class="floor-shell">@flash</div></div>
    }

    <main id="main-content" class="floor-shell">
        @RenderBody()
    </main>

    <script src="~/js/floor.js" asp-append-version="true"></script>
</body>
</html>
```

`IStringLocalizer<SharedResource> L` is injected into every view by `_ViewImports.cshtml`, so `@L["Full app"]` works here without a using.

- [ ] **Step 6: Add the scan view**

Create `Sklad.NET/Views/Floor/Index.cshtml`:

```cshtml
@model Sklad.ViewModels.FloorScanViewModel

@{
    ViewData["Title"] = L["Scan"].Value;
}

<h1 class="floor-title">@L["Scan a tire"]</h1>

@if (Model.NotFound)
{
    <p class="floor-error" role="alert">@L["No tire matches that code."]</p>
}

<form method="get" asp-action="Tire" class="floor-scan">
    <label class="field-label" for="code">@L["Scan SKU or barcode"]</label>
    <input id="code" name="code" class="field-input floor-input" value="@Model.Code"
           autocomplete="off" autocapitalize="off" spellcheck="false" autofocus />
    <button type="submit" class="btn btn-primary floor-btn">@L["Find"]</button>
</form>
```

- [ ] **Step 7: Add the booking view**

Create `Sklad.NET/Views/Floor/Tire.cshtml`. Two submit buttons share the name `movementType`: whichever the worker taps contributes its value, so a direction is chosen and the form submitted in one tap. The `Book` action does not exist until Task 2 — `asp-action="Book"` generates the URL from the route template regardless, so the view compiles and renders now.

```cshtml
@model Sklad.ViewModels.FloorBookViewModel

@{
    ViewData["Title"] = Model.Sku;
}

<div class="floor-sku">@Model.Sku</div>
<div class="floor-desc">@Model.Description &middot; @Model.Size</div>

<div class="floor-facts">
    <div class="floor-fact">
        <div class="floor-fact-num num">@Model.Quantity</div>
        <div class="floor-fact-label">@L["In stock"]</div>
    </div>
    <div class="floor-fact">
        <div class="floor-fact-num num">@(string.IsNullOrWhiteSpace(Model.Location) ? "—" : Model.Location)</div>
        <div class="floor-fact-label">@L["Location"]</div>
    </div>
</div>

<div asp-validation-summary="All" class="form-errors"></div>

<form method="post" asp-action="Book" class="floor-book">
    <input type="hidden" name="tireId" value="@Model.TireId" />

    <label class="field-label" for="quantity">@L["Quantity"]</label>
    <div class="floor-stepper" data-stepper>
        <input id="quantity" name="quantity" type="number" class="field-input floor-input"
               value="1" min="1" step="1" inputmode="numeric" required />
    </div>

    <button type="submit" name="movementType" value="In" class="btn btn-primary floor-btn">@L["In"]</button>
    <button type="submit" name="movementType" value="Out" class="btn btn-secondary floor-btn">@L["Out"]</button>

    <a asp-action="Index" class="floor-exit floor-back">@L["Scan another"]</a>
</form>
```

- [ ] **Step 8: Add the stepper script**

Create `Sklad.NET/wwwroot/js/floor.js`:

```javascript
(function () {
    document.querySelectorAll('[data-stepper]').forEach(function (stepper) {
        var input = stepper.querySelector('input[type="number"]');
        if (!input) return;

        function button(label, delta, aria) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'btn btn-secondary floor-step';
            b.textContent = label;
            b.setAttribute('aria-label', aria);
            b.addEventListener('click', function () {
                var next = (parseInt(input.value, 10) || 1) + delta;
                input.value = next < 1 ? 1 : next;
            });
            return b;
        }

        stepper.insertBefore(button('−', -1, stepper.dataset.less || 'Less'), input);
        stepper.appendChild(button('+', 1, stepper.dataset.more || 'More'));
    });
})();
```

The button labels come from `data-less` / `data-more` so Razor can localize them. Set them on the stepper div in `Tire.cshtml`:

```cshtml
    <div class="floor-stepper" data-stepper data-less="@L["Fewer"]" data-more="@L["More"]">
```

- [ ] **Step 9: Add the styles**

Append to `Sklad.NET/wwwroot/css/site.css`, immediately **before** the first `@media print` block:

```css
/* ============================================================
   Floor — one job per screen, thumb-sized targets
   ============================================================ */
.floor-shell { max-width: 34rem; margin-inline: auto; padding-inline: var(--sp-3); }
.floor-bar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--sp-3);
    padding: var(--sp-3);
    border-bottom: 1px solid var(--rule);
}
.floor-exit { font-size: var(--f--1); color: var(--ink-50); }
.floor-title { font-size: var(--f-2); margin-block: var(--sp-5) var(--sp-4); }
.floor-scan, .floor-book { display: grid; gap: var(--sp-3); }
/* iOS Safari zooms into any focused input under 16px */
.floor-input { font-size: 1rem; min-height: 3.25rem; }
.floor-btn { min-height: 3.25rem; width: 100%; font-size: var(--f-0); }
.floor-error { color: var(--red); font-weight: 600; margin-bottom: var(--sp-3); }
.floor-sku { font-size: var(--f-2); font-weight: 700; margin-top: var(--sp-5); }
.floor-desc { color: var(--ink-50); margin-top: var(--sp-1); }
.floor-facts {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--sp-3);
    margin-block: var(--sp-5);
    padding-block: var(--sp-4);
    border-block: 1px solid var(--rule-soft);
}
.floor-fact-num { font-size: var(--f-3); font-weight: 700; line-height: 1; }
.floor-fact-label { font-size: var(--f--1); color: var(--ink-50); text-transform: uppercase; letter-spacing: .04em; margin-top: var(--sp-1); }
.floor-stepper { display: flex; gap: var(--sp-2); }
.floor-stepper .floor-input { flex: 1; text-align: center; }
.floor-step { min-height: 3.25rem; min-width: 3.25rem; }
.floor-back { display: block; text-align: center; padding-block: var(--sp-3); }
```

- [ ] **Step 10: Add the Bulgarian translations**

Before adding each, check it is absent: `grep -c 'data name="Find"' Sklad.NET/Resources/SharedResource.bg.resx`. `Scan`, `Scan SKU or barcode`, `Quantity`, `Location`, `In` and `Out` already exist — **skip those six**. Add only:

```xml
  <data name="Scan a tire" xml:space="preserve">
    <value>Сканирай гума</value>
  </data>
  <data name="No tire matches that code." xml:space="preserve">
    <value>Няма гума с този код.</value>
  </data>
  <data name="Find" xml:space="preserve">
    <value>Намери</value>
  </data>
  <data name="Full app" xml:space="preserve">
    <value>Пълно приложение</value>
  </data>
  <data name="In stock" xml:space="preserve">
    <value>В наличност</value>
  </data>
  <data name="Scan another" xml:space="preserve">
    <value>Сканирай друга</value>
  </data>
  <data name="Fewer" xml:space="preserve">
    <value>По-малко</value>
  </data>
  <data name="More" xml:space="preserve">
    <value>Повече</value>
  </data>
```

- [ ] **Step 11: Run the tests and the build**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj`
Expected: PASS, 176 tests. `Resx_covers_every_localized_key` must be among them and green.
Run: `dotnet build Sklad.NET/Sklad.NET.csproj --warnaserror`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 12: Commit**

```bash
git add Sklad.NET/Controllers/FloorController.cs Sklad.NET/ViewModels/FloorScanViewModel.cs Sklad.NET/ViewModels/FloorBookViewModel.cs Sklad.NET/Views/Shared/_FloorLayout.cshtml Sklad.NET/Views/Floor Sklad.NET/wwwroot/js/floor.js Sklad.NET/wwwroot/css/site.css Sklad.NET/Resources/SharedResource.bg.resx Sklad.Tests/FloorControllerTests.cs
git commit -m "feat(floor): add the floor scan and booking screens"
```

---

### Task 2: Book In and Out movements

**Files:**
- Modify: `Sklad.NET/Controllers/FloorController.cs` (add `Book`)
- Modify: `Sklad.NET/Resources/SharedResource.bg.resx`
- Test: `Sklad.Tests/FloorControllerTests.cs` (append)

**Interfaces:**
- Consumes: `FloorBookViewModel.FromTire`, `FloorController` from Task 1. `IInventoryService.GetTireAsync`, `IInventoryService.RegisterMovementAsync`.
- Produces: `FloorController.Book(int tireId, MovementType movementType, int quantity)`.

**This is the task the whole feature turns on.** Re-read the Global Constraints, especially the `Adjustment` rule, before you start.

- [ ] **Step 1: Write the failing tests**

Append to `FloorControllerTests` (add `using Microsoft.EntityFrameworkCore;` at the top of the file for `FindAsync`):

```csharp
    [Fact]
    public async Task Book_an_In_raises_stock_and_records_the_user()
    {
        var tire = await SeedAsync(NewTire("MI-300", qty: 10));
        using var context = _db.CreateContext();

        var result = Assert.IsType<RedirectToActionResult>(
            await CreateController(context, "picker").Book(tire.Id, MovementType.In, 3));
        Assert.Equal(nameof(FloorController.Index), result.ActionName);

        await using var check = _db.CreateContext();
        Assert.Equal(13, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        var movement = Assert.Single(check.StockMovements.Where(m => m.TireId == tire.Id));
        Assert.Equal(MovementType.In, movement.MovementType);
        Assert.Equal("picker", movement.UserName);
    }

    [Fact]
    public async Task Book_an_Out_beyond_stock_leaves_the_tire_untouched()
    {
        var tire = await SeedAsync(NewTire("MI-301", qty: 2));
        using var context = _db.CreateContext();
        var controller = CreateController(context);

        var result = Assert.IsType<ViewResult>(await controller.Book(tire.Id, MovementType.Out, 5));

        Assert.Equal(nameof(FloorController.Tire), result.ViewName);
        Assert.False(controller.ModelState.IsValid);

        await using var check = _db.CreateContext();
        Assert.Equal(2, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        Assert.Empty(check.StockMovements.Where(m => m.TireId == tire.Id));
    }

    // Adjustment sets stock ABSOLUTELY and permits a quantity of zero, so a crafted
    // post would silently zero a tire. The floor books flows only.
    [Theory]
    [InlineData(MovementType.Adjustment, 0)]
    [InlineData(MovementType.Adjustment, 99)]
    public async Task Book_refuses_an_adjustment(MovementType type, int quantity)
    {
        var tire = await SeedAsync(NewTire("MI-302", qty: 4));
        using var context = _db.CreateContext();

        var result = await CreateController(context).Book(tire.Id, type, quantity);

        Assert.IsType<BadRequestResult>(result);
        await using var check = _db.CreateContext();
        Assert.Equal(4, (await check.Tires.FindAsync(tire.Id))!.Quantity);
        Assert.Empty(check.StockMovements.Where(m => m.TireId == tire.Id));
    }

    [Fact]
    public async Task Book_a_zero_quantity_is_rejected()
    {
        var tire = await SeedAsync(NewTire("MI-303", qty: 4));
        using var context = _db.CreateContext();
        var controller = CreateController(context);

        Assert.IsType<ViewResult>(await controller.Book(tire.Id, MovementType.Out, 0));
        Assert.False(controller.ModelState.IsValid);

        await using var check = _db.CreateContext();
        Assert.Equal(4, (await check.Tires.FindAsync(tire.Id))!.Quantity);
    }

    [Fact]
    public async Task Book_for_a_missing_tire_returns_to_the_scan_screen()
    {
        using var context = _db.CreateContext();

        var result = Assert.IsType<RedirectToActionResult>(
            await CreateController(context).Book(9999, MovementType.In, 1));

        Assert.Equal(nameof(FloorController.Index), result.ActionName);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj --filter FloorControllerTests`
Expected: FAIL to compile, `CS1061: 'FloorController' does not contain a definition for 'Book'`.

- [ ] **Step 3: Add the Book action**

In `Sklad.NET/Controllers/FloorController.cs`, add `using Sklad.Models;` and this action:

```csharp
    // POST: /Floor/Book
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(int tireId, MovementType movementType, int quantity)
    {
        // Adjustment sets stock absolutely and permits a quantity of zero, so a
        // crafted post would zero a tire. The floor books flows only.
        if (movementType is not (MovementType.In or MovementType.Out))
            return BadRequest();

        var tire = await _inventory.GetTireAsync(tireId);
        if (tire is null)
        {
            TempData["Flash"] = _l["That tire no longer exists."].Value;
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var newQuantity = await _inventory.RegisterMovementAsync(
                tireId, movementType, quantity, note: null, CurrentUser);
            TempData["Flash"] = _l["{0}: {1} in stock.", tire.Sku, newQuantity].Value;
            return RedirectToAction(nameof(Index));
        }
        catch (InsufficientStockException ex)
        {
            ModelState.AddModelError(string.Empty, _l["Only {0} in stock.", ex.Available]);
        }
        catch (InvalidMovementQuantityException)
        {
            ModelState.AddModelError(nameof(quantity), _l["Enter a quantity of at least 1."]);
        }
        catch (TireNotFoundException)
        {
            TempData["Flash"] = _l["That tire no longer exists."].Value;
            return RedirectToAction(nameof(Index));
        }

        return View(nameof(Tire), FloorBookViewModel.FromTire(tire));
    }
```

`_l[...]` returns a `LocalizedString`, which converts implicitly to `string` for `AddModelError`. `[ValidateAntiForgeryToken]` guards the real form; the tests call the action directly and are unaffected.

Note the two `TireNotFoundException` paths: the pre-check catches the ordinary case, and the `catch` covers a tire deleted between the check and the write.

- [ ] **Step 4: Add the Bulgarian translations**

Check each is absent first, then add:

```xml
  <data name="That tire no longer exists." xml:space="preserve">
    <value>Тази гума вече не съществува.</value>
  </data>
  <data name="{0}: {1} in stock." xml:space="preserve">
    <value>{0}: {1} в наличност.</value>
  </data>
  <data name="Only {0} in stock." xml:space="preserve">
    <value>Само {0} в наличност.</value>
  </data>
  <data name="Enter a quantity of at least 1." xml:space="preserve">
    <value>Въведете количество поне 1.</value>
  </data>
```

- [ ] **Step 5: Run the tests and the build**

Run: `dotnet test Sklad.Tests/Sklad.Tests.csproj`
Expected: PASS, 182 tests.
Run: `dotnet build Sklad.NET/Sklad.NET.csproj --warnaserror`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add Sklad.NET/Controllers/FloorController.cs Sklad.NET/Resources/SharedResource.bg.resx Sklad.Tests/FloorControllerTests.cs
git commit -m "feat(floor): book In and Out movements from the floor"
```

---

### Task 3: Documentation

**Files:**
- Modify: `TODO.md`
- Modify: `CLAUDE.md` — **edit locally, never `git add` it. It is gitignored (`.gitignore:10`).**
- Modify: `README.md`

- [ ] **Step 1: Verify the count**

Run `dotnet test Sklad.Tests/Sklad.Tests.csproj` and use the number it reports. If it is not 182, STOP and report BLOCKED rather than writing a number you did not observe.

- [ ] **Step 2: TODO.md**

Add `## [x] 12. A mobile interface for the warehouse floor (2026-07-09)` after item 11, and remove `- A mobile interface for working on the warehouse floor` from the Future ideas list, leaving three. Match items 10 and 11 in voice: prose paragraphs, no bullet lists, closing with `No migration; tests 171 → 182.`

It must say: that `/Floor` is a scan-and-book flow with its own slim layout; that the app was already responsive to 480px with coarse-pointer touch targets, so the cost being paid was taps, not breakpoints (five page loads to book one movement); that booking goes through the existing ledger so no new stock logic exists; and that `Adjustment` is rejected at the controller because it sets stock absolutely and permits a zero quantity, so omitting it from the view would not have been a control.

- [ ] **Step 3: CLAUDE.md** (edit locally, never stage)

- Bump the test count.
- Add to the `Controllers/` block: `FloorController.cs ← /Floor scan-and-book flow for phones; slim _FloorLayout; In/Out only`.
- Add to `Views/`: `Floor/*` and `Shared/_FloorLayout`.
- Add a Gotcha: `FloorController.Book` rejects any `MovementType` other than `In`/`Out`. `Adjustment` sets stock **absolutely** and `RegisterMovementAsync` permits a quantity of zero for it, so a crafted POST would zero a tire; hiding the option in the view is not a control.

- [ ] **Step 4: README.md**

Bump the test count and add the floor interface to the feature list, in the surrounding plain prose voice.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test Sklad.Tests/Sklad.Tests.csproj
git status --short          # CLAUDE.md must NOT appear
git add TODO.md README.md
git commit -m "docs: record the warehouse floor interface"
```

---

## Self-review notes

Spec coverage: routes and both screens map to Task 1; booking rules and the `Adjustment` guard to Task 2; layout, styling and localization to Task 1's steps 5-10; the docs to Task 3. Every test named in the spec appears in Task 1 or Task 2.

Test arithmetic: baseline 171. Task 1 adds 5 (176). Task 2 adds 6 — four facts plus a two-case theory (182). Task 3 adds none.

Type consistency: `FloorScanViewModel { Code, NotFound }` is produced and consumed in Task 1. `FloorBookViewModel.FromTire` is produced in Task 1 and reused on Task 2's error path. `Book(int, MovementType, int)` matches its tests exactly. `CurrentUser` mirrors `TiresController.cs:24`.

An earlier draft split Task 1 into "layout + scan" and "lookup + booking". That left the Task 1 controller holding an unused `_inventory` field and a stub `Tire` action that Task 2 immediately overwrote — dead code a reviewer would rightly reject, and churn across two commits. They are now one task, because the scan screen without a lookup is not independently useful.

One risk stated rather than hidden: Task 1's `Tire.cshtml` posts to a `Book` action that does not exist until Task 2, so tapping In or Out on that intermediate commit produces a 404. This is deliberate — no stock can be written by a half-built feature — and it is why the two tasks land in this order rather than the reverse.
