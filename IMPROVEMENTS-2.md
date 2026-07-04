# Sklad.NET — Second Review: Fixes, Hardening, and UX/UI Improvements

> **Status (2026-07-04): all items below are implemented.** Test suite grew 48 -> 82; CI green. Decisions made along the way: the decimal binder treats a comma strictly as a decimal mark (never a group separator); client validation was restored (jQuery re-included) rather than removed, with comma-aware number/range rules; the scan box got `autofocus` so hardware scanners work hands-free; Delete moved off the table rows onto the Details page; table density was deliberately kept generous (readability over compactness) after consideration; Cyrillic-insensitive search uses a connection-registered `unilower()` SQLite function because the built-in `lower()` only folds ASCII. Implementing 1.2 exposed a second latent bug in the retry loop itself (corrupted change tracker on the second retry), fixed with `ChangeTracker.Clear()`. The movements date-range filter remains a TODO.md feature idea.

Second comprehensive review, as of commit `4fb5cde` (2026-07-03). The first round (`IMPROVEMENTS.md`) is fully implemented; this document covers what a fresh pass over the code, plus a dedicated UX/UI walkthrough of every screen, turned up. Items are ordered by severity within each section. Status marks: `[ ]` proposed, `[x]` implemented.

---

## 1. Bugs (should fix)

### 1.1 `[x]` Decimal price entry is culture-fragile: dot fails under bg, comma silently means ×100 under en

**Where:** `Views/Tires/Create.cshtml:104`, `Views/Tires/Edit.cshtml:107`, decimal model binding

MVC binds `decimal` form values with the request culture. Under the default `bg-BG` culture the decimal separator is a comma, so a price typed as `189.99` fails server-side binding ("The value '189.99' is not valid") — and dot is what the keyboard's numpad produces. Worse in the other direction: under `en-GB`, a price typed as `189,99` parses *successfully* as **18 999** — .NET accepts misplaced group separators — so the tire is saved with a 100× price and nobody is told. Client-side validation would normally catch the en-GB case, but it has never run at all (see 1.6), and once 1.6 is fixed, the stock dot-only `number` rule will start rejecting the *valid* bg comma format — so 1.1 and 1.6 must land together.

**Fix:** A small `IModelBinder` for `decimal` that rejects group separators, accepts both `.` and `,` as the decimal mark, and parses invariantly; register it via a `ModelBinderProvider`. When re-enabling client validation (1.6), override `$.validator.methods.number` to accept both marks so both layers agree. Regression tests: bind `"189.99"` and `"189,99"` under both cultures, and assert `"1,899"` does not become 1899.

### 1.2 `[x]` Movement retry exhaustion escapes as a 500

**Where:** `Services/InventoryService.cs:206`, `Controllers/TiresController.cs:171`

`RegisterMovementAsync` retries concurrency conflicts, but the catch filter is `when (attempt < ConcurrencyRetries)` — on the third consecutive conflict the raw `DbUpdateConcurrencyException` propagates. The controller catches only the typed inventory exceptions, so the user gets the generic error page, violating the project rule that service exceptions never surface as 500s.

**Fix:** When retries are exhausted, throw `StaleTireException`; in the controller's `RegisterMovement` POST, catch it and add the existing localized "modified by someone else" message. Add a test that forces three consecutive conflicts.

### 1.3 `[x]` CSV export shows mojibake in Excel for Cyrillic values

**Where:** `Services/InventoryService.cs:276`

`ExportCsvAsync` returns `Encoding.UTF8.GetBytes(...)` with no byte-order mark. Excel opens BOM-less UTF-8 CSV as ANSI, so a Cyrillic `Location` such as `Рафт А-3` renders as garbage. Every workstation this app targets runs Excel in Bulgarian.

**Fix:** Prepend `Encoding.UTF8.GetPreamble()` to the returned bytes. While in the file, also quote fields containing `\r` (the `Csv()` helper currently only checks `\n`). Extend the export tests.

### 1.4 `[x]` SKU and barcode matching is case-sensitive

**Where:** `Data/SkladDbContext.cs:19`, `Services/InventoryService.cs:81`

SQLite compares text case-sensitively by default, so the unique index happily stores `ABC-1` alongside `abc-1`, and Scan (`FindByCodeAsync`) misses a tire if the operator types the SKU in the wrong case. Related: the `Contains` filters on Sku/Brand/Model are case-insensitive only for ASCII — Cyrillic brand text is matched case-sensitively.

**Fix:** Configure `COLLATE NOCASE` on `Sku` and `Barcode` (`entity.Property(t => t.Sku).UseCollation("NOCASE")`), add a migration, and add tests for mixed-case scan and duplicate detection. Cyrillic-insensitive `Contains` needs `.ToLower()` on both sides (translates to SQLite `lower()`, which handles Cyrillic) — apply to the Brand/Model filters.

### 1.5 `[x]` Duplicate-SKU race falls through to an unhandled `DbUpdateException`

**Where:** `Services/InventoryService.cs:86` and `:110`

Create/Update pre-check the SKU with `AnyAsync` and then save — two concurrent submissions can both pass the check, and the loser dies on the unique index as an uncaught `DbUpdateException` (500). Unlikely with one account, but the fix is one catch block.

**Fix:** Catch `DbUpdateException` around the save, inspect for the unique-constraint violation, and rethrow `DuplicateSkuException`.

### 1.6 `[x]` Client-side validation has never worked — jQuery is never loaded

**Where:** `Views/Shared/_ValidationScriptsPartial.cshtml`, `Views/Shared/_Layout.cshtml`

The partial loads `jquery.validate.min.js` and the unobtrusive adapter, but nothing anywhere loads jQuery itself — `_Layout.cshtml` has no script tags, and no view adds one. `wwwroot/lib/jquery/` ships the file; it just lost its `<script>` include (most likely during the Bootstrap dead-asset cleanup). Result: `jquery.validate.min.js` throws `ReferenceError: jQuery is not defined` on every form page, the console shows errors on Create/Edit/Delete/RegisterMovement/Login, and **all** validation is server-side round-trips. Required-field errors, string lengths, ranges — everything costs a full POST.

**Fix:** Either add `<script src="~/lib/jquery/dist/jquery.min.js">` as the first line of the partial (the files are already self-hosted), **together with** the number-method override from 1.1; or commit to the no-JS philosophy, delete `wwwroot/lib/` and the partial, and lean on native HTML5 validation (`required`, `min`, `max`, `maxlength` attributes are mostly already emitted). Either is defensible; half-loaded is not.

### 1.7 `[x]` Crafted culture-switch link returns a 500

**Where:** `Controllers/CultureController.cs:29`

`Set` passes the raw `returnUrl` query value to `LocalRedirect`, which *throws* on a non-local URL instead of falling back. `/Culture/Set?culture=bg-BG&returnUrl=https://evil.example` → unhandled `InvalidOperationException` → error page. Not an open redirect (LocalRedirect refuses), but an anonymous endpoint that can be made to 500 on demand.

**Fix:** Mirror `AccountController.SafeReturnUrl`: `Url.IsLocalUrl(returnUrl) ? returnUrl : "/"`. Extract the helper to one shared spot while at it.

---

## 2. Security hardening

### 2.1 `[x]` No brute-force protection on login

**Where:** `Controllers/AccountController.cs:38`, `Program.cs`

The password comparison is fixed-time, but nothing limits attempts — a bot can hammer `/Account/Login` indefinitely. ASP.NET Core's built-in rate limiter makes this cheap.

**Fix:** `AddRateLimiter` with a fixed-window policy (e.g. 10 attempts/minute per IP) applied to the login POST via `[EnableRateLimiting]`; return the login view with a localized "too many attempts" message on rejection.

### 2.2 `[x]` Auth cookie `SecurePolicy` is not pinned

**Where:** `Program.cs:24-31`

`CookieSecurePolicy` defaults to `SameAsRequest`; if the site is ever served over plain HTTP (LAN deployment), the auth cookie travels in cleartext. `Always` is correct behind HTTPS but would break a plain-HTTP LAN install, so it should be configurable.

**Fix:** Default `options.Cookie.SecurePolicy = CookieSecurePolicy.Always` outside Development, overridable via an `Auth:AllowInsecureHttp` config flag for LAN installs. Also set `options.Cookie.SameSite = SameSiteMode.Lax` explicitly for documentation value.

### 2.3 `[x]` Missing security headers

**Where:** `Program.cs` middleware pipeline

No `X-Content-Type-Options`, no `X-Frame-Options`/`frame-ancestors` (relevant: cookie auth + forms = clickjacking surface), no `Referrer-Policy`.

**Fix:** A small inline middleware setting `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: same-origin`. A full CSP is optional given the no-CDN, no-inline-script design — if added, start with `default-src 'self'`.

### 2.4 `[x]` 404s render a bare, unstyled page outside Development

**Where:** `Program.cs:59-63`

`UseExceptionHandler` styles exceptions, but plain status codes (missing tire ID, mistyped URL) return an empty 404 body.

**Fix:** `app.UseStatusCodePagesWithReExecute("/Home/Error")` (or a dedicated `/Home/NotFound` view with a "back to inventory" link, which is friendlier).

---

## 3. Operational

### 3.1 `[x]` Movement timestamps depend on the server's timezone

**Where:** `Views/Tires/Details.cshtml:112`, `Views/Movements/Index.cshtml:59`

Dates are stored UTC (correct) but displayed via `ToLocalTime()` — the *server's* local time. On the shop PC that's fine; in a UTC-configured container every timestamp silently shifts 2–3 hours.

**Fix:** Convert via a fixed `TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia")` in one shared helper (e.g. `Money`-style static or a Razor helper), used by both views.

### 3.2 `[x]` No backup story for `sklad.db`

The database is the shop's entire inventory ledger. There is no backup mechanism or documented procedure.

**Fix:** An authenticated `Admin/Backup` action that runs `VACUUM INTO` to a temp file and streams it as a download is ~20 lines and gives the owner one-click backups. At minimum, document a copy-while-stopped procedure in the README.

### 3.3 `[x]` SQLite runs in rollback-journal mode

WAL mode improves reader/writer concurrency and crash resilience. One `PRAGMA journal_mode=WAL` on startup (or in the connection string) is enough. Low priority at this scale.

---

## 4. Housekeeping

### 4.1 `[x]` Eight stale resource entries

`Resources/SharedResource.bg.resx` still carries keys from removed UI: `Legal`, `Min`, `Open Inventory`, `Privacy Policy`, `Qty`, `Save Changes`, `Track stock, movements, and low-stock alerts across every SKU.`, `Use this page to detail your site's privacy policy.` Delete them. (Coverage in the other direction is complete — every used key has a Bulgarian entry.)

### 4.2 `[x]` Test gaps

No coverage for: the Scan action (found/not-found/whitespace), the Export controller action (headers, filename, content type), the retry-exhaustion path from 1.2, the `Contains` text filters in `SearchAsync`, and `GetTireAsync(includeMovements: true)` ordering newest-first. The suite is otherwise solid at 48 tests.

Also worth one test: `TestDb` uses `EnsureCreated`, so the *migrations* are never exercised — the schema under test comes straight from the model, and a forgotten migration after a model change would sail through CI. EF Core exposes `context.Database.HasPendingModelChanges()`; a single test asserting it returns false catches model/migration drift forever.

### 4.4 `[x]` Error page still carries scaffold artifacts

**Where:** `Views/Shared/Error.cshtml:27-32`

The "Development Mode" block — advice about setting `ASPNETCORE_ENVIRONMENT`, aimed at developers — renders unconditionally, so a shop operator who hits a real error reads deployment instructions. The view also uses inline `style=` attributes, the only ones in the project, bypassing the design system.

**Fix:** Delete the Development Mode block (the developer exception page already covers dev), and replace the inline styles with a utility class.

### 4.3 `[x]` Small internal cleanups

- `.lang` CSS class doubles as the username chip container in `_Layout.cshtml:42` — give the username its own class (`.whoami`) so `.lang` means one thing.
- `RegisterMovement` POST fetches the tire twice on the error path (`TiresController.cs:173` and `:198`).
- `Views/Movements/Index.cshtml` table has no `<colgroup>`, so fixed layout splits all six columns evenly — Date and Quantity get the same width as Note. Add proportional columns.

---

## 5. UX / UI improvements

From a walkthrough of every screen in both languages, thinking in terms of the two daily jobs: *sell/receive tires fast* and *find a tire fast*.

### 5.1 `[x]` No feedback after successful actions

Creating a tire, saving an edit, and recording a movement all redirect silently — the only signals the UI ever gives are errors. An operator recording an `Out` movement has to visually diff the quantity to confirm it worked.

**Fix:** A TempData flash pattern (the `ScanMessage` plumbing already exists and can be generalized): "Tire saved", "Movement recorded — stock is now 27". Render as a quiet confirmation strip under the page head (black border, not red — red stays reserved for alerts). This is the single highest-value UX change on the list.

### 5.2 `[x]` Post-save navigation is inconsistent and loses context

Create redirects to the inventory list, so the natural next step ("did it save? add a movement?") means re-finding the tire. Edit's Back button goes to Details but a successful save goes to Index.

**Fix:** Redirect Create and Edit to the tire's Details page (with the 5.1 flash). Details is already the hub with Edit/Move actions, so nothing is lost.

### 5.3 `[x]` The tire name in the inventory table is not a link

The only path to Details is the small "View" action on the far right; the row hover highlight *suggests* clickability that isn't there. Movements already links the tire cell — Index should too.

**Fix:** Wrap the Brand/Model cell (and SKU cell) in a Details link. With the tire cell clickable, "View" in row-actions becomes redundant and can be dropped, reducing the 2×2 action grid to a cleaner single row of three.

### 5.4 `[x]` Scan box is invisible to anyone who doesn't already know it

`Views/Tires/Index.cshtml:38` — the scan input has a placeholder but no label and no button; submitting requires knowing to press Enter. It's also easy to miss that it exists at all.

**Fix:** Add a visually-hidden `<label>`, a small "Scan" submit button, and consider `autofocus` on the Index page so hardware barcode scanners (which type + Enter) work with zero clicks — with the caveat that autofocus steals keyboard focus from screen-reader users on every page load, so it may deserve a config toggle or `data-` opt-in.

### 5.5 `[x]` Movement form ignores intent the UI already knows

Every path into RegisterMovement lands on the same form with `In` preselected. Coming from Low Stock, the intent is a delivery (`In`); a sale flow wants `Out`. Each wrong preselect is a mis-recorded ledger entry waiting to happen.

**Fix:** Accept an optional `type` query parameter in the GET action to preselect the movement type; make Low Stock's "Move" pass `type=In`. Optionally add explicit "Stock in / Stock out" links in row-actions or on Details.

### 5.6 `[x]` No projected result on the movement form

The Adjustment/In/Out semantics differ (absolute vs. delta) and the hint text explains it, but the operator still does mental arithmetic. A dozen lines of vanilla JS can show "Resulting stock: 31" live under the quantity field, updating on type/quantity change — the strongest guard against the classic "entered the delta as an Adjustment" mistake. Degrades gracefully with JS off (hint text remains).

### 5.7 `[x]` Delete page warns *after* the doomed attempt

`Views/Tires/Delete.cshtml` shows a generic warning that tires with movements can't be deleted, lets the user click the red button, and only then errors. The page has the tire ID; it can know upfront.

**Fix:** In the Delete GET, check for movements and render the blocked state directly: explain why, hide/disable the delete button, and point to Adjustment-to-zero (write-off) as the correct action for a tire with history.

### 5.8 `[x]` Filter panel has no clear/reset inside it

The "Clear filter" link only appears in the results header after filtering. Inside the open filter panel, next to Search, there is no way to reset — users blank nine fields by hand.

**Fix:** Add a secondary "Clear" button (plain link to `Index`) beside Search in `filter-fields`.

### 5.9 `[x]` Details movement history is unbounded

`GetTireAsync(includeMovements: true)` loads *all* movements; a busy SKU accumulates hundreds of rows on every Details view. The global journal can't rescue it because it has no per-tire filter.

**Fix:** Take the newest ~20 in the Include, show "View all N movements" linking to the journal filtered by tire — which means adding a `tireId` filter to `GetMovementsAsync` and `/Movements`. (The date-range filter already in `TODO.md` fits the same form.)

### 5.10 `[x]` Favicon is the stock ASP.NET template icon

`wwwroot/favicon.ico` is the unmodified template icon (browsers pick it up via the `/favicon.ico` convention; there is no explicit `<link rel="icon">`). Every pinned tab shows the generic ASP.NET mark instead of the brand. Replace it with a `favicon.svg` matching the wordmark — black square, white "S", red index mark — add the `<link rel="icon">` to `_Layout.cshtml`, and keep the `.ico` as fallback.

### 5.11 `[x]` No print styles

Warehouse reality: people print stock lists and the low-stock reorder sheet. Printing today emits the sticky topbar, filter chrome, red links, and useless action columns.

**Fix:** A short `@media print` block: hide `.topbar`, `.site-footer`, `.head-actions`, `.filter`, `.row-actions`, `.pager`; force black text, allow the table to use full width. Low Stock printed as a reorder sheet is the main win.

### 5.12 `[x]` Default sort is invisible

The inventory opens sorted by brand, but no `▲` shows because the default sort key is `null`, not `"brand"`. Clicking "Tire" once appears to do nothing (it re-sorts to the same order). Treat `null` as `brand` when rendering the sort marker, or make the default explicit.

### 5.13 `[x]` Accessibility pass

- `th` elements lack `scope="col"`; sortable headers lack `aria-sort`.
- Active nav tab and language link should carry `aria-current="page"` / `aria-current="true"` — the red bar and black chip are currently color/position-only signals.
- The scan input is placeholder-only (see 5.4).
- The Movements type filter links (`block-meta`) get an `is-active` class but no styling defines it there, so the active filter is invisible — style it (e.g. black underline or bold) and add `aria-current`.

### 5.14 `[x]` Micro-polish

- Buttons and row-action links flip colors with no transition; a ~120ms `background-color/color` transition softens every interaction (reduced-motion media query already neutralizes it).
- "Delete" in row-actions looks identical to the safe actions until hover. Consider dropping Delete from the table entirely (it lives on Details) — fewer accidents, calmer rows, and with 5.3 the actions column shrinks to "Move / Edit".
- Table density: rows are generous (`--sp-2` padding both axes at 0.875rem type). Screenshots show the app being used at 80% browser zoom — a signal the data pages run large. Consider a compact table variant (reduce vertical padding to `--sp-1`) or trimming `--f-3` stat numerals on the data-heavy screens.
- The Report page's By Brand table could add a "% of value" column — the data is already in `ValueReportGroup`, and a share column makes the ranking meaningful at a glance.
- The scan input's `--f--1` (12px) font triggers iOS Safari's auto-zoom on focus (it zooms any input under 16px); pin it to 16px on touch viewports or accept the zoom.
- `InterVariable.woff2` isn't preloaded, so text renders in Helvetica first and swaps (`font-display: swap`); one `<link rel="preload" as="font" crossorigin>` in the layout removes the flash.
- `body { min-height: 100vh }` overshoots on mobile browsers with dynamic URL bars; `100dvh` (with `100vh` fallback) is the modern fix.

### 5.15 `[x]` Numeric form fields start prefilled with zeros

**Where:** `Views/Tires/Create.cshtml`, `Views/Tires/RegisterMovement.cshtml:72`

The Create action passes `new Tire()`, so every non-nullable numeric renders its default: Width, Profile, Diameter, Quantity and MinStock all show `0` (and UnitPrice `0,00`), which the operator must select-and-delete nine times per tire — the carefully written placeholders (`205`, `55`, `16`…) never show. Same on the movement form: Quantity opens as `0`, which is an *invalid* value for In/Out. This is the most-typed form in the app; the zeros fight the user on every entry.

**Fix:** Bind Create through a dedicated view model with nullable numerics (`int? Width` + `[Required]`), mapping to `Tire` in the controller — fields start blank, placeholders show, and validation still fires. Same for `RegisterMovementViewModel.Quantity`. (A cheaper patch — `value=""` overrides on the inputs — works but breaks value redisplay after a validation error.)

### 5.16 `[x]` Out-of-range page numbers aren't clamped

`SearchAsync` and `GetMovementsAsync` clamp `page` to ≥ 1 but not to the top: `/Tires?Page=999` renders an empty table with "Page 999 of 3" and a working Previous button to page 998. Harmless but sloppy — clamp to `TotalPages` after counting (or redirect to the last page).

---

## Suggested order of work

1. **1.1 + 1.6 together** (decimal binding and the dead client validation are one coherent fix), then **1.2**, **1.3**, **1.7** — the real bugs.
2. **5.1 + 5.2** (flash + redirect-to-details) — biggest UX return for the effort, touches the same controller code as 1.2.
3. **5.15** — the zero-prefilled forms; the Create view model it introduces is also where the 1.1 binder naturally attaches.
4. **2.1–2.4** — hardening, each independent and small.
5. **1.4, 1.5, 3.1** — correctness edges.
6. **5.3–5.9, 5.12, 5.13, 5.16** — UX round, mostly view-layer.
7. **3.2, 3.3, 4.x, 5.10, 5.11, 5.14** — housekeeping and polish.
