# Sklad.NET — Tire Warehouse Management System

ASP.NET Core 10 MVC tire warehouse manager with EF Core (SQLite), a bilingual Bulgarian/English UI, and dual EUR/BGN price display.

## Features

- Tire inventory CRUD with search, filters, sortable columns, and pagination
- Stock movement ledger: In / Out / Adjustment with a full audit trail and per-user attribution; stock counts change only through movements
- Barcode support: scan box on the inventory page jumps straight to a tire by SKU or barcode
- Low-stock report with deficit counts
- Global movements journal with type filter
- Stock value report grouped by brand and season
- CSV export (respects the active filter, formula-injection safe)
- Cookie sign-in (single configurable account)
- Bulgarian (default) and English UI; prices shown in EUR with BGN alongside at the fixed 1.95583 rate

## Prerequisites

- .NET 10 SDK
- `dotnet ef` global tool for migrations (`dotnet tool install --global dotnet-ef`)

## Quick start

```bash
cd Sklad.NET
dotnet run
```

The app applies migrations automatically and, in Development, seeds 15 sample tires with opening-stock movements on first start. Open `http://localhost:5246` and sign in with the development credentials from `appsettings.Development.json`: username `admin`, password `sklad-dev`.

For any non-development deployment, set real credentials via configuration (environment variables `Auth__Username` and `Auth__Password`, or user secrets). Sign-in is impossible until they are set, and sample data is not seeded outside Development.

## Tests

```bash
dotnet test Sklad.Tests/Sklad.Tests.csproj
```

xUnit tests cover the inventory service (movement rules, concurrency, search/paging, CSV escaping), the money helpers, and the controller error paths. CI runs build + tests on every push and pull request (`.github/workflows/ci.yml`).

## Database

Dev database is **SQLite** (`sklad.db` in the project folder, auto-created on first run). `Tire.Version` is a concurrency token: concurrent edits or simultaneous stock movements are detected instead of silently losing updates.

### Switch to SQL Server LocalDB

1. Install SQL Server Express with LocalDB (or Visual Studio, which includes it).
2. Add the provider package: `dotnet add package Microsoft.EntityFrameworkCore.SqlServer`.
3. In `appsettings.json` replace the connection string:
   ```json
   "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=SkladDb;Trusted_Connection=True;MultipleActiveResultSets=true"
   ```
4. In `Program.cs` replace `UseSqlite` with `UseSqlServer`.
5. Remove `Microsoft.EntityFrameworkCore.Sqlite` and `SQLitePCLRaw.lib.e_sqlite3` from the `.csproj`.
6. Remove the `CURRENT_TIMESTAMP` default for `StockMovement.Date` in `SkladDbContext.OnModelCreating` (SQL Server syntax differs; the service sets the date explicitly anyway).
7. Drop the existing migrations and recreate:
   ```bash
   dotnet ef migrations remove
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

## Project structure

```
Sklad.NET/               main web app
  Controllers/           Tires (CRUD, low stock, report, export, scan),
                         Movements (journal), Account (sign-in), Culture, Home
  Data/                  SkladDbContext, DbInitializer (dev seed)
  Helpers/               Money (EUR/BGN), Enums (localization keys)
  Migrations/            EF Core migrations (SQLite)
  Models/                Tire, StockMovement, enums
  Resources/             SharedResource + Bulgarian translations (.resx)
  Services/              IInventoryService / InventoryService, typed exceptions
  ViewModels/            filter, index, movement, login view models
  Views/                 Razor views (custom design system in wwwroot/css/site.css)
Sklad.Tests/             xUnit test suite
```
