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

## [ ] Future ideas

- Excel export (ClosedXML package)
- Supplier / purchase order tracking
- Multiple user accounts with roles (current auth is a single configured account)
- Date-range filter on the movements journal
