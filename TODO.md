# TODO — Sklad.NET feature backlog

Delivered work is marked `[x]`; open ideas are `[ ]`.

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

## [x] 5. Hardening and feature round (2026-07-03)

Barcode-wipe/duplicate-SKU/delete-guard/NRE bug fixes, ledger-only quantity changes with `Tire.Version` concurrency token, typed service exceptions with localized messages, cookie authentication with per-movement user attribution, movements journal (`/Movements`), stock value report, scan box, pagination + sorting, CSV formula-injection guard, self-hosted Inter font, dev-only seeding, DataProtection key persistence, culture validation, `Sklad.Tests` xUnit suite (48 tests), GitHub Actions CI, dead-asset cleanup (Bootstrap dist, unreachable views, SqlServer package). Also fixed two newly discovered pre-existing bugs: Bulgarian localization never resolved at runtime (assembly-name vs root-namespace mismatch), and decimal ORDER BY crashed EF's SQLite provider under the bg-BG culture.

---

## [x] 6. Second review round (2026-07-04)

Culture-safe decimal binding (dot and comma both accepted, comma never a group separator), client-side validation restored (jQuery include had been lost) with comma-aware rules, movement-retry 500 fixed plus a latent change-tracker bug in the retry loop, CSV UTF-8 BOM, NOCASE SKU/barcode collation with Cyrillic-insensitive search (`unilower`), duplicate-SKU race handling, safe culture returnUrl, login rate limiting, pinned cookie policy, security headers, styled 404, flash confirmations with redirect-to-details, blank-start numeric forms, page clamping, tire links in tables, scan button + autofocus, movement type preselect and live stock projection, delete pre-warn, filter clear, per-tire movements journal with capped Details history, Europe/Sofia timestamps, a11y round (scope/aria-sort/aria-current), database backup download (`VACUUM INTO`), WAL mode, brand favicon, print styles, share-of-value report column, and housekeeping. Tests 48 → 82.

---

## [x] 7. Inventory filter dropdowns and movements date range (2026-07-05)

Brand/width/profile/diameter filters became dropdowns listing only values present in stock (`GetFilterOptionsAsync`), plus new location and low-stock-only filters carried through sort, paging, and CSV export. The movements journal gained a from/to date filter interpreting days in shop time (Europe/Sofia) against UTC-stored dates. Tests 82 → 94.

---

## [x] 8. Excel export, suppliers/purchase orders, multi-user roles (2026-07-06)

All three backlog features implemented:

- **Excel export (ClosedXML)**: `ExcelExportService` produces .xlsx workbooks for the inventory (filter- and sort-aware, `/Tires/ExportExcel`) and the movements journal (filter-aware, `/Movements/Export`) with localized bold headers, frozen header row, autofilter, EUR/BGN price columns, stock value column, shop-time date cells, and capped auto-fit column widths.
- **Suppliers / purchase orders**: `Supplier` (unique NOCASE name) and `PurchaseOrder`/`PurchaseOrderItem` models with a Draft → Ordered → Received / Cancelled lifecycle in `PurchasingService`. Receiving an order applies `In` movements through the ledger (tire quantity + version + movement rows in one save, with concurrency retry) and stamps the movement note with the PO number and supplier. Draft-only editing, cancel guards, supplier delete blocked while orders exist, tire delete blocked while referenced by order lines. Dynamic line-item form with blank-row pruning and client-side line totals.
- **Multi-user accounts with roles**: `AppUser` (PBKDF2 via `PasswordHasher`, unique NOCASE username, security stamp) replaces the single configured account; `UserService` handles credential validation (timing-equalized), user CRUD, last-admin and self-delete guards. Cookies carry a security-stamp claim validated on every request, so password/role changes and deletions end sessions immediately. Roles: `Admin` (user management, backup, tire/supplier deletion) and `User` (day-to-day operations). First run seeds an admin from the existing `Auth:Username`/`Auth:Password` configuration. Styled 403 page for denied access.

Migration `SuppliersOrdersUsers`; tests 94 → 129.

---

## [x] 9. Quick-reorder a tire into a pre-filled purchase order (2026-07-08)

An "Order from supplier" action on a tire's Details page and an "Order" link in the Low Stock list open the purchase order Create form with the tire pre-selected on the first order line. When the tire is below its minimum, the line quantity is pre-filled with the deficit (MinStock - Quantity) so restocking is one confirmation away. Create (GET) accepts an optional `tireId` and falls back to a blank line for an unknown id. Suite now at 135 tests.

---

## [x] 10. Shop settings and the purchase-order print document (2026-07-09)

`/PurchaseOrders/Print/{id}` renders an order as a document rather than an app
page: shop letterhead, supplier block, order lines, total units, EUR/BGN total,
and signature lines for who ordered and who received. It prints through the
existing `data-print` handler, so staff save a PDF from the browser; no PDF
library was added, which also sidesteps embedding a Cyrillic TTF.

The letterhead needed a shop identity, and none existed. `ShopSettings` is a
singleton row (name, address, VAT/EIK, phone, email) behind an Admin-only page
at `/ShopSettings`. Every field is optional and blanks store as null, so an
unconfigured install prints a valid order with no letterhead. The service
returns a blank settings object when the row is absent, because `TestDb` builds
its schema with `EnsureCreated` and never runs migrations — a seeded row would
be invisible to every test.

Groundwork in the same round: `LocalizationTests` now fails on any `L["..."]`
key missing from the Bulgarian resx. It previously only checked that the
satellite assembly loaded, so an untranslated string shipped silently.

Migration `ShopSettings`; tests 135 → 148.

---

## [x] 11. Charted reports over a date range (2026-07-09)

`/Tires/Report` now draws what the warehouse holds and how it moves. Value by
brand and value by season are charted beside the tables that already
summarised them, and a new movement chart sets units received against units
shipped over a range the user picks. The range buckets by day up to sixty days
and by month beyond that, so a short range reads as a week's work and a long
one as a season.

Adjustments are excluded from the movement bars and counted separately as
corrections. An adjustment's quantity is the new absolute stock level rather
than an amount moved, so adding it to a flow would be meaningless. Buckets are
cut on Europe/Sofia calendar days, not UTC ones, which means a movement
recorded at 22:30 UTC belongs to the next shop day.

Chart.js 4.4.9 is vendored into `wwwroot/lib` beside jQuery rather than pulled
from a CDN. Chart data reaches the browser through a JSON island serialized
with the default `System.Text.Json` encoder, which escapes `<`, so a tire brand
named `</script>` cannot break out of the script tag. Charts draw with
animation off, because the Print button calls `window.print()` immediately and
a canvas ignores the stylesheet's reduced-motion rule.

No migration; tests 148 → 171.

---

## [x] 12. A mobile interface for the warehouse floor (2026-07-09)

`/Floor` is a scan-and-book flow for a picker standing at the rack with a
phone: one screen to scan or type a SKU or barcode, a second screen showing
the tire, its location, and its current stock, with a quantity stepper and
large In / Out buttons. It runs under its own slim `_FloorLayout`, selected
by `Views/Floor/_ViewStart.cshtml`, with no topbar, filters, or footer to get
in the way of a thumb.

The app was already responsive down to 480px with touch targets sized for a
coarse pointer, so this round was not about breakpoints. The cost being paid
was taps: booking a single movement through the desktop screens meant five
page loads (search, open the tire, open the movement form, fill it in,
confirm), and the floor screen collapses that to two. Booking itself reuses
`RegisterMovementAsync` unchanged, so no new stock logic exists anywhere in
the feature; the ledger, the stock guard, the concurrency retry, and the
user attribution are all the same code the desktop movement form already
calls.

`FloorController.Book` accepts a movement type from the form post, and
`Adjustment` sets stock absolutely rather than moving it, with a permitted
quantity of zero for write-offs. Leaving `Adjustment` off the view's buttons
would not have stopped a crafted POST from reaching it, so the controller
checks the movement type against an allow-list of `In` and `Out` before
calling the service at all.

Driving the running app then found what the tests could not, because they
call the action directly and never exercise model binding. The type bound
non-nullable, so a missing, unparseable or out-of-range value failed binding
and fell back to `default(MovementType)`, which is `In`: posting
`movementType=bogus` silently booked a movement. Binding a nullable enum
makes the absence representable, and the allow-list then rejects it.
`TiresController.RegisterMovement` was never affected, because it checks
`ModelState.IsValid`.

No migration; tests 171 → 184.

---

## [x] 13. Integrity, service boundaries, and camera scanning (2026-07-12)

Purchase orders now carry their own optimistic-concurrency token. Editing lines,
marking an order, receiving it, and cancelling it all require the version shown
to the operator, so a stale screen cannot receive obsolete lines or overwrite a
concurrent state change. Direct movements and receipts share checked stock
arithmetic and reject an `Int32` overflow atomically.

SQLite's ASCII-only `NOCASE` collation was replaced on identifiers with a
.NET-backed `UNICODE_NOCASE`, covering Cyrillic SKU/barcode lookups, usernames,
and supplier uniqueness. The migration has an upgrade test that starts from the
previous schema with related data, applies the migration, and checks both data
preservation and the new uniqueness behavior.

`InventoryService` no longer owns report generation or CSV formatting. Reports
and CSV are separate services; filter/sort translation, pagination, and stock
arithmetic each have one shared implementation. The duplicate POST submit guard
in the desktop and floor scripts was similarly consolidated.

The `/Floor` scan screen progressively exposes camera barcode scanning through
the browser's native `BarcodeDetector`. Unsupported or denied-camera browsers
retain the existing manual/physical-scanner flow. The same round fixed the floor
concurrency 500, fully localized default validation messages and display names,
and the missing floor heading/skip link. Tests 193 → 223.

Migration `InventoryIntegrity`; 223 tests.

---

## [ ] Future ideas

- Email notifications on low stock
- Barcode label printing
- A move to a more powerful server database if data volume grows

The existing architecture allows these extensions without substantially
reworking current code.
