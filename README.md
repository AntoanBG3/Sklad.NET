# Sklad.NET — Tire Warehouse Management System

ASP.NET Core 10 MVC skeleton with Entity Framework Core, Razor views, and Bootstrap.

## Prerequisites

- .NET 10 SDK
- `dotnet ef` global tool (`dotnet tool install --global dotnet-ef`)

## Quick start

```bash
cd Sklad.NET/Sklad.NET
dotnet run
```

The app runs migrations and seeds 15 sample tires automatically on first start.
Open `http://localhost:5246` — it redirects straight to the tire inventory.

## Database

The default dev database is **SQLite** (`sklad.db` in the project folder, auto-created on first run).

### Switch to SQL Server LocalDB

1. Install SQL Server Express with LocalDB (or install Visual Studio which includes it).
2. In `appsettings.json` replace the connection string:
   ```json
   "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=SkladDb;Trusted_Connection=True;MultipleActiveResultSets=true"
   ```
3. In `Program.cs` replace `UseSqlite` with `UseSqlServer`.
4. Remove `Microsoft.EntityFrameworkCore.Sqlite` and `SQLitePCLRaw.lib.e_sqlite3` from the `.csproj`.
5. Drop the existing migration and recreate it:
   ```bash
   dotnet ef migrations remove
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```

## Project structure

```
Sklad.NET/
  Controllers/   TiresController (CRUD), HomeController
  Data/          SkladDbContext, DbInitializer (seed)
  Migrations/    EF Core migrations
  Models/        Tire, StockMovement, enums (Season, TireType, MovementType)
  Services/      IInventoryService (TODO stubs)
  ViewModels/    (empty, ready for filter/report view models)
  Views/
    Tires/       Index, Create, Edit, Details, Delete
    Shared/      _Layout, Error
```

## TODO — features to implement next

1. **Search / filter** (`TiresController.Index`)
   Add a `TireFilterViewModel` with fields for SKU, Brand, Model, Width/Profile/Diameter, Season, and Type. Bind it in the Index action and apply `.Where()` clauses before the query executes. Render the form above the table in `Views/Tires/Index.cshtml`.

2. **Stock movement registration** (`TiresController.RegisterMovement` + `IInventoryService.RegisterMovementAsync`)
   POST action that creates a `StockMovement` record and adjusts `Tire.Quantity` in a single transaction. Rules: `In` adds, `Out` subtracts (reject if it would go negative), `Adjustment` sets directly. Display movement history on the Details page.

3. **Low-stock inventory report** (`TiresController.LowStock`)
   GET action returning tires where `Quantity <= MinStock`. Reuse the Index view or a dedicated read-only view. The Index table already highlights low-stock rows in yellow (`table-warning`).

4. **CSV / Excel export** (`TiresController.Export` + `IInventoryService.ExportCsvAsync`)
   Return a `FileContentResult` with `text/csv` and a timestamped filename. For Excel, add `ClosedXML` or `EPPlus` and write a `.xlsx`. Wire up an Export button on the Index page.
