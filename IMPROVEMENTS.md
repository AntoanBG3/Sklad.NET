# Sklad.NET — Code Analysis: Fixes, Improvements, and Suggested Changes

> **Status (2026-07-03): all items below are implemented**, including the section 8 feature backlog (authentication, movements journal, scan flow, sorting, value report, DataProtection persistence). Section 4.3 needed no code change beyond the culture-safe price sort; the provider-collation difference it describes is documented in CLAUDE.md. See `TODO.md` item 5 for the summary and the test suite in `Sklad.Tests` for regression coverage.
>
> Implementation surfaced two pre-existing bugs this review had missed, both fixed: (a) **Bulgarian localization never worked at runtime** — the assembly name (`Sklad.NET`) differs from the root namespace (`Sklad`), so the localizer looked for resources under the wrong name; fixed with `[assembly: RootNamespace]` plus moving `SharedResource.cs` out of the `Resources/` folder (see CLAUDE.md). (b) **Sorting by a decimal column crashes EF's SQLite provider under comma-decimal cultures** like bg-BG; price sorting uses a REAL cast and report grouping orders in memory.

Comprehensive review of the codebase as of commit `5cd7c4f` (2026-07-03). The project builds with zero warnings, all four planned features work, and localization coverage in the views is complete (every `@L["..."]` key has a Bulgarian entry in `SharedResource.bg.resx`). The items below are ordered by severity within each section, with file references and a suggested fix for each.

---

## 1. Bugs (should fix)

### 1.1 Editing a tire silently erases its barcode — data loss

**Where:** `Controllers/TiresController.cs:83` (Edit POST), `Models/Tire.cs:17`

The Edit `[Bind]` whitelist omits `Barcode`, so the model binder leaves it `null` on the posted entity. The action then calls `_db.Update(tire)`, which marks **every** property modified, including the unbound `Barcode`. Result: saving any edit overwrites the tire's barcode with `NULL`. All 15 seeded tires have barcodes; one round-trip through the Edit form destroys them.

**Fix:** Either add `Barcode` to the bind list and the Create/Edit forms (it is a real field users will eventually want), or load the tracked entity and copy only the bound values onto it (`TryUpdateModelAsync` or manual assignment) instead of the blind `Update`. The second option also fixes 1.4 below.

### 1.2 Duplicate SKU causes an unhandled 500 error

**Where:** `Controllers/TiresController.cs:67` and `:90`, `Data/SkladDbContext.cs:19`

`Tire.Sku` has a unique index. Creating or editing a tire with an existing SKU throws `DbUpdateException` from `SaveChangesAsync`, which nothing catches — the user gets the generic error page and loses their form input. This is an easy state for a warehouse operator to hit.

**Fix:** Pre-check with `AnyAsync(t => t.Sku == tire.Sku && t.Id != tire.Id)` and add a localized ModelState error ("A tire with this SKU already exists."), or catch the `DbUpdateException` and translate it. Add the message to `SharedResource.bg.resx`.

### 1.3 Deleting a tire with movement history causes an unhandled 500 error

**Where:** `Controllers/TiresController.cs:112` (DeleteConfirmed), FK configured `DeleteBehavior.Restrict` in `SkladDbContext.cs:26`

The Delete confirmation page even warns "Tires with existing movement records cannot be deleted" (`Views/Tires/Delete.cshtml:24`), but the POST doesn't enforce or handle it — the Restrict FK makes `SaveChangesAsync` throw and the user gets the error page.

**Fix:** Check `AnyAsync(m => m.TireId == id)` on `StockMovements` before removing; if movements exist, return to the Delete view with a localized ModelState error. Alternatively offer a "deactivate" (soft delete) flag, which is usually what a warehouse actually wants.

### 1.4 Concurrent edits and movements can silently lose updates

**Where:** `Controllers/TiresController.cs:89` (blind `Update`), `Services/InventoryService.cs:38-67` (read-modify-write on `Quantity`)

There is no concurrency token on `Tire`. Two operators registering `Out` movements at the same time both read the same quantity, both pass the negative-stock guard, and the stock count ends up wrong (oversell). Similarly, Edit is last-write-wins over any movement that happened between GET and POST. SQLite's single-writer model makes this unlikely in dev, but on SQL Server in production with several users it is a genuine risk. The existing `DbUpdateConcurrencyException` handler in Edit only fires for deleted rows, so it gives false confidence.

**Fix:** Add a `[Timestamp] byte[] RowVersion` to `Tire` (works on SQL Server; for SQLite use a manually incremented version column), include it as a hidden field in Edit, and handle the conflict with a friendly message. For `RegisterMovementAsync`, either rely on the row version with a retry, or issue an atomic conditional update (`UPDATE Tires SET Quantity = Quantity - @q WHERE Id = @id AND Quantity >= @q` via `ExecuteUpdateAsync`) inside the same transaction as the movement insert.

### 1.5 NullReferenceException in RegisterMovement when the tire disappears mid-flow

**Where:** `Controllers/TiresController.cs:143-160` (POST), `Views/Tires/RegisterMovement.cshtml:4-5`

On a failed-validation or failed-service POST, the action re-renders the view with `ViewBag.Tire = await _db.Tires.FindAsync(vm.TireId)` — which is `null` if the tire was deleted in another tab or the posted `TireId` is stale. The view immediately casts and dereferences it (`tire.Brand`), producing a 500.

**Fix:** After the `FindAsync`, return `NotFound()` when the tire is null, in both re-display paths. Longer term, pass the tire in the view model instead of `ViewBag` so the compiler can see the nullability.

### 1.6 Validation contradictions around movement quantity

**Where:** `ViewModels/RegisterMovementViewModel.cs:16`, `Models/StockMovement.cs:16`, `Services/InventoryService.cs:44`

Three layers disagree: the view model allows `Quantity >= 0`, the service rejects `0` for In/Out (server round-trip, error appears late), and the `StockMovement` entity declares `[Range(1, ...)]` even though the service legitimately saves an Adjustment movement with `Quantity = 0` (write-off). EF Core doesn't validate annotations on save, so this works today, but any future validation pass (or a scaffolded API) would reject a valid write-off record.

**Fix:** Relax `StockMovement.Quantity` to `[Range(0, int.MaxValue)]`, and consider client-side conditional validation (min 1 unless Adjustment is selected) so In/Out with 0 fails before the round-trip.

---

## 2. Localization gaps

### 2.1 Service error messages reach the user in English only

**Where:** `Services/InventoryService.cs:42, 45, 51-52`; surfaced via ModelState in `TiresController.cs:157`

"Tire not found.", "Quantity must be at least 1 for In/Out movements.", and "Insufficient stock. Available: X, requested: Y." are hardcoded English strings displayed verbatim in the (default-Bulgarian) UI. These are the messages an operator is most likely to see during daily work.

**Fix:** Inject `IStringLocalizer<SharedResource>` into `InventoryService` (or throw typed exceptions and localize in the controller, which keeps the service culture-agnostic). Add the strings with `{0}`-style placeholders to `SharedResource.bg.resx`.

### 2.2 Movement dates display raw UTC without time

**Where:** `Views/Tires/Details.cshtml:105`

`m.Date.ToString("dd MMM yyyy")` shows the UTC date with no time component. Two movements on the same day are indistinguishable, and near midnight the displayed date is off by a day for Bulgarian users (UTC+2/+3).

**Fix:** Convert to local time (store the warehouse timezone or use `TimeZoneInfo`) and include the time: `dd MMM yyyy HH:mm`. Month names already follow the current culture, which is correct.

---

## 3. Robustness and security hardening

### 3.1 CSV output is not guarded against spreadsheet formula injection

**Where:** `Services/InventoryService.cs:82-85`

`Csv()` escapes quotes, commas, and newlines but not leading `=`, `+`, `-`, `@`. SKU, brand, model, and location are user input; a value like `=HYPERLINK(...)` executes when the exported file is opened in Excel. Low risk for an internal tool, cheap to fix.

**Fix:** Prefix cells starting with `=`, `+`, `-`, `@` with a single quote (or a tab) in `Csv()`.

### 3.2 Culture endpoint accepts arbitrary values via GET

**Where:** `Controllers/CultureController.cs:8-18`

`Set` writes whatever culture string arrives into a one-year cookie without checking it against the two supported cultures, and it mutates state on a GET. Request localization falls back gracefully for unknown cultures, so impact is minor, but validating keeps the cookie clean.

**Fix:** Accept only `bg-BG`/`en-GB` (compare against the configured list); optionally make it a POST with antiforgery, as the ASP.NET Core docs sample does.

### 3.3 Google Fonts loaded from CDN

**Where:** `Views/Shared/_Layout.cshtml:15-17`

Inter is fetched from `fonts.googleapis.com` on every page. The app is otherwise fully self-contained: this breaks typography when offline (a warehouse floor is exactly where connectivity drops), and transmitting visitor IPs to Google has been ruled a GDPR violation by EU courts (relevant for a Bulgarian deployment).

**Fix:** Self-host the Inter woff2 files under `wwwroot/fonts/` with a local `@font-face` block in `site.css`.

### 3.4 Startup migration and seeding are unconditional

**Where:** `Program.cs:55-60`

`Database.Migrate()` plus seeding on every start is right for dev, but in production with more than one instance it races, and auto-seeding sample tires into a production database on first run would be a surprise.

**Fix:** Gate seeding on `app.Environment.IsDevelopment()`; for production, run migrations as a deployment step (or accept migrate-on-start but document the single-instance assumption).

---

## 4. Performance (matters as data grows)

### 4.1 Index statistics load every tire into memory

**Where:** `Controllers/TiresController.cs:29-40`

The stats band materializes `{Quantity, MinStock, UnitPrice}` for the whole table and aggregates in C#. Fine at 15 rows; wasteful at 10,000.

**Fix:** Aggregate in the database — a single grouped query or `CountAsync`/`SumAsync` calls. `LowStockCount` translates directly: `CountAsync(t => t.Quantity <= t.MinStock)`.

### 4.2 No pagination on the inventory table

**Where:** `TiresController.Index`, `Views/Tires/Index.cshtml`

Already on the TODO list. Every tire renders on one page; combined with 4.1 this is the first thing to hurt at scale. Skip/Take with a page-size of ~50 and a simple pager keeps the current architecture intact.

### 4.3 Substring search behaves differently per provider

**Where:** `Services/InventoryService.cs:21-23`

`Contains` translates to `LIKE '%x%'`: it can't use the SKU index, and SQLite's `LIKE` is case-insensitive only for ASCII while SQL Server's follows collation. A search for a Cyrillic brand name would be case-sensitive on dev SQLite and case-insensitive on prod SQL Server — a subtle behavioral difference to be aware of when switching providers. Acceptable now; consider normalized (upper-cased) shadow columns if search becomes hot.

---

## 5. Dead code and cleanup

- **Stale TODO comments** in `Services/IInventoryService.cs:5-20` — every "TODO" there is implemented; the comments now actively mislead. Delete them and keep plain doc summaries.
- **Unused Bootstrap distribution** (~2 MB, 40+ files) in `wwwroot/lib/bootstrap/` — the layout links only `site.css`; no view uses Bootstrap classes. Remove the folder (jquery + jquery-validation are still needed for unobtrusive validation; `jquery.slim` variants can also go).
- **Unreachable views**: `Views/Home/Index.cshtml` (HomeController.Index redirects to Tires and the default route pattern never lands on it) and `Views/Home/Privacy.cshtml` (no link anywhere). Remove them, or make Privacy reachable from the footer if it is wanted for compliance.
- **`Views/Shared/_Layout.cshtml.css`** — scoped CSS from the template; it is only served through `Sklad.NET.styles.css`, which the layout never references. Dead file.
- **`wwwroot/js/site.js`** — empty template stub, and the layout doesn't include it. Remove (or wire it in when JS is actually needed).
- **Unused SQL Server provider package** — `Microsoft.EntityFrameworkCore.SqlServer` in the .csproj drags `Azure.Identity`, `Microsoft.Data.SqlClient`, and a dozen other assemblies into the output while `UseSqlite` is the only code path. Remove it until the production switch actually happens (README documents the steps to add it back).
- **`Export` action duplication** — `TiresController.Export:164-171` branches on `HasAnyFilter` to reproduce the same ordering `SearchAsync` already applies. Calling `SearchAsync` unconditionally (all-null filters return everything) deletes the branch.
- **"+0" deficit display** — `Views/Tires/LowStock.cshtml:69,82`: a tire exactly at minimum shows a deficit of "+0". Either display "—" for zero or define low stock as strictly below minimum.
- **Stale README** — `README.md` still documents the old nested `Sklad.NET/Sklad.NET` path, lists all four implemented features as future TODOs, calls ViewModels "empty", and describes the UI as Bootstrap. It contradicts both the code and CLAUDE.md; rewrite it.

---

## 6. Architecture and design suggestions

### 6.1 Direct quantity edits bypass the movement ledger

**Where:** `TiresController.Edit` binds `Quantity`; `Views/Tires/Edit.cshtml` exposes it

The domain rule says movements are the audit trail and `Tire.Quantity` is the live count they explain. The Edit form breaks that invariant: an operator can change the count with no ledger record, after which the movement history no longer adds up. This is the most consequential design decision on the list.

**Suggestion:** Remove `Quantity` from the Edit form and bind list (keep it on Create as the opening balance, ideally recorded as an initial `Adjustment` movement). Point users at Register Movement for count changes. If direct edits must stay, auto-record an `Adjustment` movement whenever Edit changes the quantity.

### 6.2 Controller talks to DbContext and service inconsistently

`TiresController` uses `IInventoryService` for search/movements/export but raw `SkladDbContext` for CRUD and stats. Workable at this size, but the split means invariants (like SKU uniqueness handling) have no single home. Consider moving Create/Edit/Delete and the stats query into the service so the controller only orchestrates, which also makes unit testing (7.1) natural.

### 6.3 Expose the Barcode field

The field exists in the model, migration, and seed data but no form or view shows it. For a tire warehouse, barcode lookup is the killer input method (USB scanners type the code + Enter). Minimum: add it to Create/Edit forms and the search filter. Natural follow-up: a single "scan box" on Index that matches SKU or barcode exactly.

---

## 7. Testing and tooling (currently absent)

### 7.1 No test project

The solution has zero tests. `InventoryService.RegisterMovementAsync` is dense, pure business logic (In/Out/Adjustment rules, negative-stock guard, quantity validation) and ideally suited to unit tests with the SQLite in-memory provider. The CSV escaping and `Money` rounding are also table-driven-test material. Suggested start: an xUnit project `Sklad.Tests` with ~10 tests over the service and helpers, added to the .slnx.

### 7.2 No CI

No GitHub Actions workflow exists. A minimal `dotnet build --warnaserror` + `dotnet test` workflow on push/PR would catch regressions and keep the zero-warning state honest.

### 7.3 No logging

Nothing in the app logs domain events (movements, deletions, failed stock operations). `ILogger<T>` is already available via DI; a handful of log statements in `InventoryService` would make production issues diagnosable.

---

## 8. Feature backlog (beyond TODO.md's list)

These extend the "Future ideas" section of `TODO.md` (Excel export, pagination, authentication, suppliers/purchase orders):

1. **Authentication first.** Anyone who can reach the URL can delete inventory and falsify the ledger. Even single-role cookie auth (ASP.NET Core Identity) should precede other features for any real deployment; it is also a prerequisite for recording *who* made each movement (`StockMovement.UserId`).
2. **Global movement journal** — a `/Movements` page listing recent movements across all tires with date/type filters. The data model already supports it; it is the natural companion to the per-tire timeline.
3. **Barcode-driven receiving/dispatch flow** — scan, pick In/Out, enter quantity, done (builds on 6.3).
4. **Sorting on the Index table** — column-header sorting is a small addition to `SearchAsync` and pairs with pagination.
5. **Stock value report** — the stats band already computes total value; a per-brand/per-season breakdown is one `GroupBy` away and useful for insurance/accounting.
6. **Data protection persistence** — if deployed behind multiple instances or containers, configure `DataProtection` key persistence so culture cookies and antiforgery tokens survive restarts.

---

## Suggested order of attack

| Priority | Items | Rationale |
|----------|-------|-----------|
| Now | 1.1, 1.2, 1.3, 1.5 | User-visible crashes and silent data loss in daily flows |
| Next | 2.1, 6.1, 1.6, 5 (cleanup), README rewrite | Correct UX in the default language, protect the ledger invariant, remove misleading dead weight |
| Soon | 7.1, 7.2, 1.4, 3.1-3.4 | Safety net before further feature work; hardening |
| Later | 4.x, 6.2, 6.3, 8.x | Scale and feature growth |
