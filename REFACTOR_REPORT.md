# Sklad.NET Refactor Report

Date: 12 July 2026

Baseline: `master` at `995dfd3`

Repository: <https://github.com/AntoanBG3/Sklad.NET>

Scope: architecture, correctness, data integrity, localization, accessibility,
mobile warehouse UX, migrations, and tests

## Executive summary

This refactor keeps the existing ASP.NET Core MVC application and its workflows,
but strengthens the boundaries around inventory, purchasing, database identity,
and user-facing validation.

The most important outcomes are:

- purchase orders can no longer receive obsolete lines or overwrite a concurrent
  edit/status change;
- valid large stock values no longer wrap into negative `Int32` totals;
- Cyrillic identifiers are genuinely case-insensitive;
- schema upgrades detect old Unicode collisions before SQLite rebuilds tables;
- reporting and CSV formatting are no longer responsibilities of the main
  inventory service;
- the warehouse-floor screen can scan a barcode with a supported phone camera;
- Bulgarian validation is complete, including field names;
- the suite increased from 193 to 223 passing tests.

No feature was intentionally removed. Existing inventory, movement, purchasing,
export, backup, authentication, and reporting routes remain in place.

## How to run the application

### Prerequisites

- .NET 10 SDK
- Git
- A modern browser
- Optional: Node.js, only for maintainers who want to syntax-check the JavaScript
- Optional: `dotnet-ef`, only when creating or editing migrations

Confirm the SDK:

```powershell
dotnet --version
```

The output must begin with `10.`. If the command reports .NET 8 or an older
version, install the .NET 10 SDK before continuing.

On the machine where this refactor was prepared, .NET 10 is already installed
under the current user's local application data, while the system `dotnet`
command still resolves to .NET 8. Use the installed SDK explicitly:

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet --version
```

The reported version should be `10.0.301` or another `10.x` release. As an
alternative, prepend that directory to `PATH` for the current PowerShell
session:

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
dotnet --version
```

### Start from the current local checkout

From the repository root:

#### Windows Command Prompt (`cmd.exe`)

Use this form when the prompt looks like `C:\Users\...>`:

```batch
set "DOTNET=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
"%DOTNET%" --version
"%DOTNET%" restore .\Sklad.NET.slnx
"%DOTNET%" run --project .\Sklad.NET\Sklad.NET.csproj --launch-profile http
```

#### PowerShell

Use this form when the prompt begins with `PS`:

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet restore .\Sklad.NET.slnx
& $dotnet run --project .\Sklad.NET\Sklad.NET.csproj --launch-profile http
```

Open:

<http://localhost:5246>

Stop the application with `Ctrl+C` in the terminal.

### Development login

On a fresh Development database, the configured bootstrap account is:

```text
Username: admin
Password: sklad-dev
```

These values come from `Sklad.NET/appsettings.Development.json`. They create the
first administrator only when the `Users` table is empty. If the database
already contains users, sign in with an existing account instead.

Development also seeds sample tires and opening movements on the first run.
The SQLite database is `Sklad.NET/sklad.db`; migrations run automatically at
startup.

Do not commit production credentials or a production database. Use environment
variables, user secrets, or an untracked production settings file for deployment.

## How to verify the refactor

Run the same build and test checks used for the handoff:

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet restore .\Sklad.NET.slnx
& $dotnet build .\Sklad.NET.slnx --configuration Release --no-restore --warnaserror
& $dotnet test .\Sklad.NET.slnx --configuration Release --no-build
& $dotnet format analyzers .\Sklad.NET.slnx --verify-no-changes --severity warn
```

Optional JavaScript syntax checks:

```powershell
node --check .\Sklad.NET\wwwroot\js\floor.js
node --check .\Sklad.NET\wwwroot\js\form-guards.js
node --check .\Sklad.NET\wwwroot\js\site.js
```

Verified result for this refactor:

```text
Release build: 0 warnings, 0 errors
Tests:         223 passed, 0 failed, 0 skipped
Analyzers:     clean
JavaScript:    syntax checks passed
```

The running application was also checked in a browser for login, Bulgarian
validation markup, lowercase SKU lookup, the mobile floor workflow, responsive
layout, the camera fallback, accessibility structure, and console errors.

## Detailed change log

### 1. Service boundaries and maintainability

The original `InventoryService` was 439 lines and mixed catalog CRUD, filtering,
stock writes, reporting, pagination, and CSV formatting.

After the refactor it is 258 physical lines. The extracted responsibilities
are:

- `InventoryReportService` / `IInventoryReportService` for warehouse statistics,
  value reports, and movement trends;
- `InventoryCsvExportService` / `IInventoryCsvExportService` for localized,
  formula-safe CSV output;
- `TireQuery` for the shared filter and sort vocabulary;
- `Pagination` for consistent page clamping;
- `StockQuantity` for the shared non-negative, non-overflowing stock invariant.

This leaves the main service focused on inventory catalog and stock-ledger
behavior, while preserving the existing controller routes.

### 2. Purchase-order optimistic concurrency

`PurchaseOrder.Version` is now an EF Core concurrency token.

Every aggregate mutation carries the expected version from the rendered form to
the service:

- edit draft lines;
- mark as ordered;
- receive stock;
- cancel the order.

Missing or malformed HTTP version tokens are rejected as bad requests. A stale
version produces a typed `StalePurchaseOrderException` and a localized message.

This prevents races such as:

- receiving old line quantities while another user edits the draft;
- cancelling an order while another user receives it;
- two editors silently overwriting one another.

Tire concurrency conflicts remain distinct and continue to use the existing
retry policy.

### 3. Stock overflow and 64-bit reporting

Direct incoming movements and purchase-order receipts now use the same checked
stock arithmetic. A result above `Int32.MaxValue` raises the typed
`StockQuantityOverflowException` before a movement/order is committed.

Purchase-order receiving preflights every line before mutating tracked tires,
including duplicate lines for the same tire. The operation remains atomic.

Warehouse unit totals, report group totals, and movement trend buckets now use
`long`, so multiple individually valid rows cannot overflow the report into a
negative number or HTTP 500.

### 4. Unicode-aware identifiers

SQLite's built-in `NOCASE` collation only folds ASCII. The previous code therefore
treated Latin identifiers case-insensitively but allowed Cyrillic pairs such as:

```text
Админ
аДМИН
```

The connection interceptor now registers `.NET`-backed `UNICODE_NOCASE`, and the
model applies it to:

- tire SKU;
- tire barcode;
- supplier name;
- username.

Regression tests cover Cyrillic lookup, login, and duplicate rejection.

### 5. Safe `InventoryIntegrity` migration

The new migration adds `PurchaseOrder.Version` and changes identifier collations.

Before any SQLite table rebuild, it creates temporary Unicode-aware unique
indexes as a compatibility preflight. If old data contains case-only Cyrillic
duplicates, the migration fails before adding columns, renaming tables, or
writing its migration-history entry.

Resolution procedure for a collision:

1. Back up the database.
2. Rename, merge, or remove one conflicting old-schema record.
3. Start the application again.

The migration is retryable. Tests prove both paths:

- related data survives a successful upgrade from the previous migration;
- a Unicode collision leaves the old schema intact, then succeeds after repair.

### 6. Bulgarian validation and localization

ASP.NET Core previously localized explicitly supplied messages but left the
framework's default DataAnnotations text partially English.

The new validation adapter supplies stable resource keys for:

- `Required`;
- `Range`;
- `StringLength`;
- `MinLength`;
- `EmailAddress`.

Validated view-model fields now have localized display keys. The test suite scans
all view models and fails if a future validated field lacks a Bulgarian display
name or validation message.

### 7. Mobile camera barcode scanning

`/Floor` progressively exposes a **Scan with camera** action when the browser
supports both `BarcodeDetector` and `getUserMedia`.

The implementation:

- prefers the rear-facing camera;
- detects common retail/industrial barcode formats;
- fills and submits the existing SKU/barcode lookup;
- moves keyboard focus between Start and Stop controls;
- stops the camera on navigation;
- clears stale BFCache status;
- falls back silently to manual input or a physical scanner when unsupported;
- shows a localized error while preserving manual input if permission is denied.

No external scanning library or CDN dependency was added.

### 8. Shared form protection and dirty-state correctness

The duplicate desktop/floor double-submit code is now one `form-guards.js`
implementation.

It emits `sklad:valid-submit` only after validation succeeds. Dirty forms clear
their unsaved state on that event, so an invalid client-side submit no longer
disables the navigation warning.

Adding or removing purchase-order rows emits a bubbling input event, ensuring
those structural edits are also protected by the unsaved-changes warning.

### 9. Floor workflow hardening and accessibility

The warehouse-floor booking action now handles:

- exhausted tire concurrency retries;
- incoming stock overflow;
- existing insufficient-stock and validation failures.

These errors re-render the compact workflow instead of producing HTTP 500.

The successful tire screen now has a real level-one heading, and the floor layout
has the same skip link as the desktop layout.

### 10. Test expansion

The test suite increased from 193 to 223 cases. New coverage includes:

- Unicode SKU/barcode, supplier, and username behavior;
- purchase-order stale line/status operations;
- missing HTTP concurrency preconditions;
- stock overflow atomicity through services and controllers;
- 64-bit warehouse/report/trend totals;
- floor stale and overflow recovery;
- localized default validation and display names;
- accessible floor structure;
- successful migration with existing related data;
- safe Unicode-collision failure, repair, and retry.

## Original codebase / Fable assessment

### Overall rating: 8/10

The original codebase was not poor-quality spaghetti. It was a strong, compact
monolith with several unusually thoughtful details.

What was done well:

- 193 baseline tests using real SQLite behavior;
- optimistic concurrency for tires;
- atomic inventory ledger writes;
- typed domain exceptions;
- authentication, role checks, security stamps, rate limiting, and security
  headers;
- localized Bulgarian/English UI and Sofia-time reporting;
- CSV/Excel safety and formatting;
- responsive, print, accessibility, and mobile-floor work;
- automatic migrations, backups, and zero-warning CI.

Why it was not rated 9 or 10:

- purchase orders lacked an aggregate concurrency boundary;
- stock and report totals could overflow;
- documented Cyrillic case-insensitivity was only ASCII-insensitive in the
  database;
- default validation remained visibly mixed-language;
- `InventoryService` had accumulated too many responsibilities;
- controller tests did not cover the full ASP.NET Core binding/middleware pipeline;
- some floor concurrency paths could return HTTP 500;
- dynamic browser behavior had no automated JavaScript/E2E suite.

Authorship caveat: all 114 baseline commits are attributed by Git to
`AntoanBG3`. There is no Fable/Claude co-author metadata, so this report rates the
result associated with Fable, not independently verified authorship.

## Known limitations after the refactor

- `InventoryService` still combines catalog and ledger operations; it is much
  smaller, but those could be separated in a future larger architecture pass.
- Camera scanning depends on native browser support and a secure context
  (`https://` or `localhost`). Manual input and physical scanners remain the
  compatibility path, including when a phone opens a plain-HTTP LAN address.
- `UNICODE_NOCASE` uses ordinal Unicode case folding, not canonical Unicode
  normalization. Visually equivalent precomposed and decomposed identifiers are
  not automatically merged. Raw SQLite tools also need to register an equivalent
  collation before querying or writing the indexed identifier columns.
- A single tire's stored quantity remains a 32-bit integer by design; checked
  writes now reject values outside that range, while cross-row aggregates use
  64-bit integers.
- Optimistic purchase-order conflicts are deliberately not auto-merged. The user
  must reload the latest order and reapply the intended change.
- The project still lacks a full `WebApplicationFactory`/browser automation suite
  for authentication, antiforgery, routing, rate limiting, and model binding.
- SQLite is appropriate for the current single-shop/single-instance scope, but a
  multi-instance deployment should move to a server database.
- Low-stock email notifications and barcode label printing remain future ideas.

## Recommended GitHub handoff

A draft pull request is the best way to send this work for review. It preserves a
complete diff, runs CI, allows inline comments, and keeps the original repository
unchanged until the owner merges it.

The current checkout is on `master` with uncommitted changes. The GitHub CLI on
this machine is signed in as `warbladebg`, which has read but not push permission
on `AntoanBG3/Sklad.NET`; no fork currently exists. Use the fork workflow below.

### 1. Create a branch and commit locally

```powershell
git switch -c codex/inventory-integrity-refactor
git add -A
git status --short
git commit -m "refactor: harden inventory workflows and add camera scanning"
```

### 2. Create a GitHub fork and push the branch

```powershell
gh repo fork AntoanBG3/Sklad.NET --remote --remote-name fork
git push -u fork codex/inventory-integrity-refactor
```

### 3. Open a draft pull request

```powershell
gh pr create `
  --repo AntoanBG3/Sklad.NET `
  --base master `
  --head warbladebg:codex/inventory-integrity-refactor `
  --draft `
  --title "Refactor inventory integrity and add camera scanning" `
  --body-file REFACTOR_REPORT.md
```

Send the resulting pull-request URL to the reviewer. They can read this report in
the PR, inspect every changed line, run the application, and leave inline feedback.

Do not send `bin/`, `obj/`, `sklad.db`, Data Protection keys, or production
configuration. They are already ignored by the repository and must remain local.
