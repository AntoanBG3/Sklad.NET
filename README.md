# Sklad.NET — Tire Warehouse Management System

ASP.NET Core 10 MVC tire warehouse manager with EF Core (SQLite), a bilingual Bulgarian/English UI, and dual EUR/BGN price display.

## Features

- Tire inventory CRUD with search, sortable columns, and pagination; filter dropdowns (brand, width, profile, diameter) list only values actually in stock, plus season, type, location, and low-stock-only filters
- Stock movement ledger: In / Out / Adjustment with a full audit trail and per-user attribution; stock counts change only through movements, with optimistic-concurrency protection against simultaneous edits
- Barcode support: scan box on the inventory page jumps straight to a tire by SKU or barcode (Cyrillic- and case-insensitive)
- Low-stock report with deficit counts
- Global movements journal with type, per-tire, and date-range filters (dates interpreted in shop time, Europe/Sofia)
- Stock value report grouped by brand and season, with share-of-value breakdown
- CSV export (respects the active filter, formula-injection safe, UTF-8 BOM and a `sep=,` header so Excel opens it correctly in any locale) and Excel export (ClosedXML .xlsx with formatted headers, typed price/date cells, and EUR/BGN columns) for both the inventory and the movements journal
- Supplier management and purchase orders with a Draft → Ordered → Received / Cancelled lifecycle; receiving an order books the stock through the movement ledger with the PO number in the audit trail
- One-click database backup download (consistent `VACUUM INTO` snapshot, no downtime)
- Multi-user accounts with Admin/User roles: hashed passwords, per-IP login rate limiting, and immediate session invalidation on password or role changes; admin-only user management, backups, and deletions
- Bulgarian (default) and English UI; prices shown in EUR with BGN alongside at the fixed 1.95583 rate
- Print-friendly styles, styled error/404 pages, unsaved-changes and double-submit guards

## Prerequisites

- .NET 10 SDK
- `dotnet ef` global tool for migrations (`dotnet tool install --global dotnet-ef`)

## Quick start

```bash
cd Sklad.NET
dotnet run
```

The app applies migrations automatically and, in Development, seeds 15 sample tires with opening-stock movements on first start. Open `http://localhost:5246` and sign in with the development credentials from `appsettings.Development.json`: username `admin`, password `sklad-dev`.

Accounts live in the database. On first start with an empty `Users` table, an administrator account is created from the `Auth:Username`/`Auth:Password` configuration (environment variables `Auth__Username` and `Auth__Password`, or user secrets); after that, manage accounts from the **Users** page (admin only). Sign-in is impossible until the first admin exists, and sample data is not seeded outside Development. If the app is served over plain HTTP on a trusted LAN, set `Auth__AllowInsecureHttp=true`; otherwise the auth cookie is marked secure-only and sign-in will not stick without HTTPS.

## Tests

```bash
dotnet test Sklad.Tests/Sklad.Tests.csproj
```

The xUnit suite (132 tests) covers the inventory service (movement rules, concurrency, search/filtering/paging, CSV escaping and localized headers), the purchasing service (order lifecycle, receive-to-ledger, guards), the user service (hashing, credential validation, last-admin/self-delete guards, session invalidation), Excel workbook contents, controller error paths, flexible decimal binding, Bulgarian resource coverage, the money helpers and tag helper, and the backup endpoint. CI runs build (warnings as errors) + tests on every push and pull request (`.github/workflows/ci.yml`).

## Database

Dev database is **SQLite** (`sklad.db` in the project folder, auto-created on first run, WAL journal mode). `Tire.Version` is a concurrency token: concurrent edits or simultaneous stock movements are detected instead of silently losing updates. A consistent backup can be downloaded from the stock value report page at any time.

### Switch to SQL Server LocalDB

1. Install SQL Server Express with LocalDB (or Visual Studio, which includes it).
2. Add the provider package: `dotnet add package Microsoft.EntityFrameworkCore.SqlServer`.
3. In `appsettings.json` replace the connection string:
   ```json
   "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=SkladDb;Trusted_Connection=True;MultipleActiveResultSets=true"
   ```
4. In `Program.cs` replace `UseSqlite` with `UseSqlServer` and remove the SQLite-specific pieces (`SqliteFunctionsInterceptor`, the WAL `PRAGMA`).
5. Remove `Microsoft.EntityFrameworkCore.Sqlite` and `SQLitePCLRaw.lib.e_sqlite3` from the `.csproj`.
6. Remove the `CURRENT_TIMESTAMP` default for `StockMovement.Date` in `SkladDbContext.OnModelCreating` (SQL Server syntax differs; the service sets the date explicitly anyway).
7. Drop the existing migrations and recreate:
   ```bash
   dotnet ef migrations remove
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

Note: case/Cyrillic-insensitive search relies on a custom SQLite function (`unilower`); on SQL Server, replace those queries with an appropriate collation.

## Project structure

```
Sklad.NET/               main web app
  Controllers/           Tires (CRUD, low stock, report, export, scan),
                         Movements (journal + Excel export), Suppliers,
                         PurchaseOrders (lifecycle), Users (admin),
                         Maintenance (backup), Account (sign-in), Culture, Home
  Data/                  SkladDbContext, DbInitializer (dev seed)
  Helpers/               Money (EUR/BGN), Enums (localization keys),
                         Dates (shop-time display), Redirects (safe returnUrl)
  Migrations/            EF Core migrations (SQLite)
  ModelBinding/          flexible decimal binder (dot or comma)
  Models/                Tire, StockMovement, Supplier, PurchaseOrder(+Item),
                         AppUser, enums
  Resources/             SharedResource + Bulgarian translations (.resx)
  Services/              InventoryService, PurchasingService, UserService,
                         ExcelExportService, typed exceptions
  TagHelpers/            <money> dual-currency cell
  ViewModels/            filter, index, create/edit, movement, order, supplier,
                         user, login view models
  Views/                 Razor views (custom design system in wwwroot/css/site.css)
  wwwroot/js/            site.js page behaviors, locale-aware validation rules
Sklad.Tests/             xUnit test suite
```

## License

MIT — see [LICENSE](LICENSE).
