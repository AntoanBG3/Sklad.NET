# TODO — Sklad.NET feature backlog

Status markers: [ ] not started, [~] in progress, [x] done

---

## [x] Foundation skeleton

Full CRUD for Tire, EF Core with SQLite, seed data (15 tires), Bootstrap UI. Done.

---

## [x] 1. Search and filter on the inventory index

`ViewModels/TireFilterViewModel.cs` created with all nullable fields including `HasAnyFilter` computed property. `IndexViewModel` wraps filter + results + warehouse stats (total SKUs, units, low-stock count, total value). `TiresController.Index` delegates to `IInventoryService.SearchAsync`. Filter panel is collapsible in the Index view; auto-expands when filters are active. Stats cards displayed above the table.

---

## [x] 2. Stock movement registration

`ViewModels/RegisterMovementViewModel.cs` created. `Services/InventoryService.cs` implements all 4 interface methods. Registered as scoped in `Program.cs`. `In` adds, `Out` subtracts with negative-stock guard, `Adjustment` sets absolute (allows 0). Both the movement record and `Tire.Quantity` update in the same `SaveChangesAsync`. `RegisterMovement` GET/POST added to controller. `Views/Tires/RegisterMovement.cshtml` created. `Details.cshtml` updated with movement timeline.

---

## [x] 3. Low-stock inventory report

`TiresController.LowStock` action added, calls `IInventoryService.GetLowStockAsync`. `Views/Tires/LowStock.cshtml` created — shows deficit count per tire (MinStock - Quantity). "Low Stock" nav link added to `_Layout.cshtml` with amber hover styling.

---

## [x] 4. CSV / Excel export

`IInventoryService.ExportCsvAsync` implemented with invariant-culture decimal formatting to avoid locale comma issues. `TiresController.Export` action respects optional filter query string. "Export CSV" button added to Index toolbar next to "Add Tire". Returns `text/csv` with date-stamped filename.

---

## [x] 5. Hardening and feature round (from IMPROVEMENTS.md, 2026-07-03)

All items from `IMPROVEMENTS.md` implemented: barcode-wipe/duplicate-SKU/delete-guard/NRE bug fixes, ledger-only quantity changes with `Tire.Version` concurrency token, typed service exceptions with localized messages, cookie authentication with per-movement user attribution, movements journal (`/Movements`), stock value report, scan box, pagination + sorting, CSV formula-injection guard, self-hosted Inter font, dev-only seeding, DataProtection key persistence, culture validation, `Sklad.Tests` xUnit suite (48 tests), GitHub Actions CI, dead-asset cleanup (Bootstrap dist, unreachable views, SqlServer package). Also fixed two newly discovered pre-existing bugs: Bulgarian localization never resolved at runtime (assembly-name vs root-namespace mismatch), and decimal ORDER BY crashed EF's SQLite provider under the bg-BG culture.

---

## [x] 6. Second review round (from IMPROVEMENTS-2.md, 2026-07-04)

All 38 items from `IMPROVEMENTS-2.md` implemented: culture-safe decimal binding (dot and comma both accepted, comma never a group separator), client-side validation restored (jQuery include had been lost) with comma-aware rules, movement-retry 500 fixed plus a latent change-tracker bug in the retry loop, CSV UTF-8 BOM, NOCASE SKU/barcode collation with Cyrillic-insensitive search (`unilower`), duplicate-SKU race handling, safe culture returnUrl, login rate limiting, pinned cookie policy, security headers, styled 404, flash confirmations with redirect-to-details, blank-start numeric forms, page clamping, tire links in tables, scan button + autofocus, movement type preselect and live stock projection, delete pre-warn, filter clear, per-tire movements journal with capped Details history, Europe/Sofia timestamps, a11y round (scope/aria-sort/aria-current), database backup download (`VACUUM INTO`), WAL mode, brand favicon, print styles, share-of-value report column, and housekeeping. Tests 48 → 82.

---

## [x] 7. Inventory filter dropdowns and movements date range (2026-07-05)

Brand/width/profile/diameter filters became dropdowns listing only values present in stock (`GetFilterOptionsAsync`), plus new location and low-stock-only filters carried through sort, paging, and CSV export. The movements journal gained a from/to date filter interpreting days in shop time (Europe/Sofia) against UTC-stored dates. Tests 82 → 94.

---

## [ ] Future ideas

- Excel export (ClosedXML package)
- Supplier / purchase order tracking
- Multiple user accounts with roles (current auth is a single configured account)
