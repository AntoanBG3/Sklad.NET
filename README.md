# Sklad.NET — Tire Warehouse Management System

ASP.NET Core 10 MVC tire warehouse manager with EF Core (SQLite), a bilingual Bulgarian/English UI, and dual EUR/BGN price display.

## Features

- Tire inventory CRUD with search, sortable columns, and pagination; filter dropdowns (brand, width, profile, diameter) list only values actually in stock, plus season, type, location, and low-stock-only filters
- Stock movement ledger: In / Out / Adjustment with a full audit trail and per-user attribution; stock counts change only through movements, with optimistic-concurrency protection against simultaneous edits
- Barcode support: scan box on the inventory page jumps straight to a tire by SKU or barcode (Cyrillic- and case-insensitive)
- Low-stock report with deficit counts
- Global movements journal with type, per-tire, and date-range filters (dates interpreted in shop time, Europe/Sofia)
- Stock value report grouped by brand and season, with share-of-value breakdown
- CSV export (respects the active filter, formula-injection safe, UTF-8 BOM for Excel)
- One-click database backup download (consistent `VACUUM INTO` snapshot, no downtime)
- Cookie sign-in (single configurable account) with per-IP login rate limiting
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

For any non-development deployment, set real credentials via configuration (environment variables `Auth__Username` and `Auth__Password`, or user secrets). Sign-in is impossible until they are set, and sample data is not seeded outside Development. If the app is served over plain HTTP on a trusted LAN, set `Auth__AllowInsecureHttp=true`; otherwise the auth cookie is marked secure-only and sign-in will not stick without HTTPS.

## Tests

```bash
dotnet test Sklad.Tests/Sklad.Tests.csproj
```

The xUnit suite (94 tests) covers the inventory service (movement rules, concurrency, search/filtering/paging, CSV escaping), controller error paths, flexible decimal binding, Bulgarian resource coverage, the money helpers and tag helper, and the backup endpoint. CI runs build (warnings as errors) + tests on every push and pull request (`.github/workflows/ci.yml`).

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
                         Movements (journal), Maintenance (backup),
                         Account (sign-in), Culture, Home
  Data/                  SkladDbContext, DbInitializer (dev seed)
  Helpers/               Money (EUR/BGN), Enums (localization keys),
                         Dates (shop-time display), Redirects (safe returnUrl)
  Migrations/            EF Core migrations (SQLite)
  ModelBinding/          flexible decimal binder (dot or comma)
  Models/                Tire, StockMovement, enums
  Resources/             SharedResource + Bulgarian translations (.resx)
  Services/              IInventoryService / InventoryService, typed exceptions
  TagHelpers/            <money> dual-currency cell
  ViewModels/            filter, index, create/edit, movement, login view models
  Views/                 Razor views (custom design system in wwwroot/css/site.css)
  wwwroot/js/            site.js page behaviors, locale-aware validation rules
Sklad.Tests/             xUnit test suite
```

## License

MIT — see [LICENSE](LICENSE).
