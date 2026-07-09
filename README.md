# Sklad.NET: Tire Warehouse Management System

An ASP.NET Core 10 MVC application for running a tire shop's stock. It uses EF Core with SQLite, a bilingual Bulgarian and English interface, and shows every price in EUR with BGN alongside.

## Features

- Tire inventory with create, edit, delete, search, sortable columns, and pagination. Filter dropdowns for brand, width, profile, and diameter list only values that are actually in stock, and there are separate filters for season, type, location, and low stock.
- A stock movement ledger with In, Out, and Adjustment entries. Every movement is recorded with the user who made it, and stock counts change only through movements. Concurrent edits are caught by optimistic concurrency instead of silently overwriting each other.
- Barcode lookup: the scan box on the inventory page jumps straight to a tire by SKU or barcode, and it handles Cyrillic and mixed case.
- A low-stock report that shows how many units each tire is short.
- A global movements journal filtered by type, by tire, and by date range, with dates read in shop time (Europe/Sofia).
- A stock value report grouped by brand and season, showing each group's share of total value.
- CSV export and Excel export for both the inventory and the movements journal. The CSV respects the active filter, guards against formula injection, and starts with a `sep=,` line and a UTF-8 BOM so Excel opens it correctly under any locale. The Excel file (ClosedXML) has formatted headers, typed price and date cells, and separate EUR and BGN columns.
- Supplier records and purchase orders that move through a Draft, Ordered, Received, or Cancelled lifecycle. Receiving an order books the stock through the same movement ledger and records the purchase order number in the audit trail.
- One-click database backup download using a consistent `VACUUM INTO` snapshot, with no downtime.
- Multi-user accounts with Admin and User roles. Passwords are hashed, sign-in is rate limited per IP, and changing a password or role invalidates that user's session immediately. User management, backups, and deletions are restricted to admins.
- Bulgarian by default with an English option throughout. Prices are stored in EUR and shown with BGN at the fixed 1.95583 rate.
- Print-friendly styles, styled error and 404 pages, and unsaved-changes and double-submit guards on forms.

## Prerequisites

- .NET 10 SDK
- The `dotnet ef` global tool for migrations (`dotnet tool install --global dotnet-ef`)

## Quick start

```bash
cd Sklad.NET
dotnet run
```

The app applies migrations automatically and, in Development, seeds 15 sample tires with opening-stock movements on first start. Open `http://localhost:5246` and sign in with the development credentials from `appsettings.Development.json`: username `admin`, password `sklad-dev`.

Accounts live in the database. On first start with an empty `Users` table, an administrator account is created from the `Auth:Username` and `Auth:Password` configuration (environment variables `Auth__Username` and `Auth__Password`, or user secrets). After that, manage accounts from the **Users** page, which is admin only. Sign-in is impossible until the first admin exists, and sample data is not seeded outside Development. If the app is served over plain HTTP on a trusted LAN, set `Auth__AllowInsecureHttp=true`; otherwise the auth cookie is marked secure-only and sign-in will not stick without HTTPS.

## Tests

```bash
dotnet test Sklad.Tests/Sklad.Tests.csproj
```

The xUnit suite has 148 tests. They cover the inventory service (movement rules, concurrency, search, filtering, paging, CSV escaping, and localized headers), the purchasing service (order lifecycle, receive-to-ledger, and guards), the user service (hashing, credential validation, last-admin and self-delete guards, and session invalidation), the Excel workbook contents, controller error paths, flexible decimal binding, Bulgarian resource coverage (every localized key is asserted present in the resx), the money helpers and tag helper, and the backup endpoint. CI builds with warnings as errors and runs the suite on every push and pull request (`.github/workflows/ci.yml`).

## Database

The development database is **SQLite** (`sklad.db` in the project folder, created on first run, WAL journal mode). `Tire.Version` is a concurrency token, so concurrent edits or simultaneous stock movements are detected rather than losing an update. A consistent backup can be downloaded from the stock value report page at any time.

### Switch to SQL Server LocalDB

1. Install SQL Server Express with LocalDB (or Visual Studio, which includes it).
2. Add the provider package: `dotnet add package Microsoft.EntityFrameworkCore.SqlServer`.
3. In `appsettings.json` replace the connection string:
   ```json
   "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=SkladDb;Trusted_Connection=True;MultipleActiveResultSets=true"
   ```
4. In `Program.cs` replace `UseSqlite` with `UseSqlServer` and remove the SQLite-specific pieces (`SqliteFunctionsInterceptor` and the WAL `PRAGMA`).
5. Remove `Microsoft.EntityFrameworkCore.Sqlite` and `SQLitePCLRaw.lib.e_sqlite3` from the `.csproj`.
6. Remove the `CURRENT_TIMESTAMP` default for `StockMovement.Date` in `SkladDbContext.OnModelCreating` (SQL Server syntax differs, and the service sets the date explicitly anyway).
7. Drop the existing migrations and recreate them:
   ```bash
   dotnet ef migrations remove
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

Note: the case-insensitive and Cyrillic-insensitive search relies on a custom SQLite function (`unilower`). On SQL Server, replace those queries with an appropriate collation.

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

MIT. See [LICENSE](LICENSE).
