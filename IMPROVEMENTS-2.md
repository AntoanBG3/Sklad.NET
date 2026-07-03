# Sklad.NET — Second Review: Fixes, Hardening, and UX/UI Improvements

Second comprehensive review, as of commit `4fb5cde` (2026-07-03). The first round (`IMPROVEMENTS.md`) is fully implemented; this document covers what a fresh pass over the code, plus a dedicated UX/UI walkthrough of every screen, turned up. Items are ordered by severity within each section. Status marks: `[ ]` proposed, `[x]` implemented.

---

## 1. Bugs (should fix)

### 1.1 `[ ]` Decimal price entry is broken under the Bulgarian culture

**Where:** `Views/Tires/Create.cshtml:104`, `Views/Tires/Edit.cshtml:107`, model binding + `_ValidationScriptsPartial`

MVC binds `decimal` form values with `CultureInfo.CurrentCulture`. Under the default `bg-BG` culture the decimal separator is a comma, so a price typed as `189.99` fails server-side binding ("The value '189.99' is not valid"). But jQuery client validation's `number` rule is dot-only, so `189,99` is rejected client-side before the form even submits. Under the default Bulgarian UI, **no decimal price passes both layers** — only whole-number prices work. English culture (`en-GB`) is unaffected, which is why this survived testing.

**Fix:** Bind `UnitPrice` invariantly and accept both separators: a small `IModelBinder` for `decimal` that normalizes `,` to `.` and parses with `CultureInfo.InvariantCulture`, registered via a `ModelBinderProvider`; plus a one-line jQuery validator override (`$.validator.methods.number`) in `_ValidationScriptsPartial` that accepts a comma. Add a regression test that binds `"189,99"` and `"189.99"` under `bg-BG`.

### 1.2 `[ ]` Movement retry exhaustion escapes as a 500

**Where:** `Services/InventoryService.cs:206`, `Controllers/TiresController.cs:171`

`RegisterMovementAsync` retries concurrency conflicts, but the catch filter is `when (attempt < ConcurrencyRetries)` — on the third consecutive conflict the raw `DbUpdateConcurrencyException` propagates. The controller catches only the typed inventory exceptions, so the user gets the generic error page, violating the project rule that service exceptions never surface as 500s.

**Fix:** When retries are exhausted, throw `StaleTireException`; in the controller's `RegisterMovement` POST, catch it and add the existing localized "modified by someone else" message. Add a test that forces three consecutive conflicts.

### 1.3 `[ ]` CSV export shows mojibake in Excel for Cyrillic values

**Where:** `Services/InventoryService.cs:276`

`ExportCsvAsync` returns `Encoding.UTF8.GetBytes(...)` with no byte-order mark. Excel opens BOM-less UTF-8 CSV as ANSI, so a Cyrillic `Location` such as `Рафт А-3` renders as garbage. Every workstation this app targets runs Excel in Bulgarian.

**Fix:** Prepend `Encoding.UTF8.GetPreamble()` to the returned bytes. While in the file, also quote fields containing `\r` (the `Csv()` helper currently only checks `\n`). Extend the export tests.

### 1.4 `[ ]` SKU and barcode matching is case-sensitive

**Where:** `Data/SkladDbContext.cs:19`, `Services/InventoryService.cs:81`

SQLite compares text case-sensitively by default, so the unique index happily stores `ABC-1` alongside `abc-1`, and Scan (`FindByCodeAsync`) misses a tire if the operator types the SKU in the wrong case. Related: the `Contains` filters on Sku/Brand/Model are case-insensitive only for ASCII — Cyrillic brand text is matched case-sensitively.

**Fix:** Configure `COLLATE NOCASE` on `Sku` and `Barcode` (`entity.Property(t => t.Sku).UseCollation("NOCASE")`), add a migration, and add tests for mixed-case scan and duplicate detection. Cyrillic-insensitive `Contains` needs `.ToLower()` on both sides (translates to SQLite `lower()`, which handles Cyrillic) — apply to the Brand/Model filters.

### 1.5 `[ ]` Duplicate-SKU race falls through to an unhandled `DbUpdateException`

**Where:** `Services/InventoryService.cs:86` and `:110`

Create/Update pre-check the SKU with `AnyAsync` and then save — two concurrent submissions can both pass the check, and the loser dies on the unique index as an uncaught `DbUpdateException` (500). Unlikely with one account, but the fix is one catch block.

**Fix:** Catch `DbUpdateException` around the save, inspect for the unique-constraint violation, and rethrow `DuplicateSkuException`.

---

## 2. Security hardening

### 2.1 `[ ]` No brute-force protection on login

**Where:** `Controllers/AccountController.cs:38`, `Program.cs`

The password comparison is fixed-time, but nothing limits attempts — a bot can hammer `/Account/Login` indefinitely. ASP.NET Core's built-in rate limiter makes this cheap.

**Fix:** `AddRateLimiter` with a fixed-window policy (e.g. 10 attempts/minute per IP) applied to the login POST via `[EnableRateLimiting]`; return the login view with a localized "too many attempts" message on rejection.

### 2.2 `[ ]` Auth cookie `SecurePolicy` is not pinned

**Where:** `Program.cs:24-31`

`CookieSecurePolicy` defaults to `SameAsRequest`; if the site is ever served over plain HTTP (LAN deployment), the auth cookie travels in cleartext. `Always` is correct behind HTTPS but would break a plain-HTTP LAN install, so it should be configurable.

**Fix:** Default `options.Cookie.SecurePolicy = CookieSecurePolicy.Always` outside Development, overridable via an `Auth:AllowInsecureHttp` config flag for LAN installs. Also set `options.Cookie.SameSite = SameSiteMode.Lax` explicitly for documentation value.

### 2.3 `[ ]` Missing security headers

**Where:** `Program.cs` middleware pipeline

No `X-Content-Type-Options`, no `X-Frame-Options`/`frame-ancestors` (relevant: cookie auth + forms = clickjacking surface), no `Referrer-Policy`.

**Fix:** A small inline middleware setting `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: same-origin`. A full CSP is optional given the no-CDN, no-inline-script design — if added, start with `default-src 'self'`.

### 2.4 `[ ]` 404s render a bare, unstyled page outside Development

**Where:** `Program.cs:59-63`

`UseExceptionHandler` styles exceptions, but plain status codes (missing tire ID, mistyped URL) return an empty 404 body.

**Fix:** `app.UseStatusCodePagesWithReExecute("/Home/Error")` (or a dedicated `/Home/NotFound` view with a "back to inventory" link, which is friendlier).

---

## 3. Operational

### 3.1 `[ ]` Movement timestamps depend on the server's timezone

**Where:** `Views/Tires/Details.cshtml:112`, `Views/Movements/Index.cshtml:59`

Dates are stored UTC (correct) but displayed via `ToLocalTime()` — the *server's* local time. On the shop PC that's fine; in a UTC-configured container every timestamp silently shifts 2–3 hours.

**Fix:** Convert via a fixed `TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia")` in one shared helper (e.g. `Money`-style static or a Razor helper), used by both views.

### 3.2 `[ ]` No backup story for `sklad.db`

The database is the shop's entire inventory ledger. There is no backup mechanism or documented procedure.

**Fix:** An authenticated `Admin/Backup` action that runs `VACUUM INTO` to a temp file and streams it as a download is ~20 lines and gives the owner one-click backups. At minimum, document a copy-while-stopped procedure in the README.

### 3.3 `[ ]` SQLite runs in rollback-journal mode

WAL mode improves reader/writer concurrency and crash resilience. One `PRAGMA journal_mode=WAL` on startup (or in the connection string) is enough. Low priority at this scale.

---

## 4. Housekeeping

### 4.1 `[ ]` Eight stale resource entries

`Resources/SharedResource.bg.resx` still carries keys from removed UI: `Legal`, `Min`, `Open Inventory`, `Privacy Policy`, `Qty`, `Save Changes`, `Track stock, movements, and low-stock alerts across every SKU.`, `Use this page to detail your site's privacy policy.` Delete them. (Coverage in the other direction is complete — every used key has a Bulgarian entry.)

### 4.2 `[ ]` Test gaps

No coverage for: the Scan action (found/not-found/whitespace), the Export controller action (headers, filename, content type), the retry-exhaustion path from 1.2, the `Contains` text filters in `SearchAsync`, and `GetTireAsync(includeMovements: true)` ordering newest-first. The suite is otherwise solid at 48 tests.

### 4.3 `[ ]` Small internal cleanups

- `.lang` CSS class doubles as the username chip container in `_Layout.cshtml:42` — give the username its own class (`.whoami`) so `.lang` means one thing.
- `RegisterMovement` POST fetches the tire twice on the error path (`TiresController.cs:173` and `:198`).
- `Views/Movements/Index.cshtml` table has no `<colgroup>`, so fixed layout splits all six columns evenly — Date and Quantity get the same width as Note. Add proportional columns.

---

## 5. UX / UI improvements

From a walkthrough of every screen in both languages, thinking in terms of the two daily jobs: *sell/receive tires fast* and *find a tire fast*.

### 5.1 `[ ]` No feedback after successful actions

Creating a tire, saving an edit, and recording a movement all redirect silently — the only signals the UI ever gives are errors. An operator recording an `Out` movement has to visually diff the quantity to confirm it worked.

**Fix:** A TempData flash pattern (the `ScanMessage` plumbing already exists and can be generalized): "Tire saved", "Movement recorded — stock is now 27". Render as a quiet confirmation strip under the page head (black border, not red — red stays reserved for alerts). This is the single highest-value UX change on the list.

### 5.2 `[ ]` Post-save navigation is inconsistent and loses context

Create redirects to the inventory list, so the natural next step ("did it save? add a movement?") means re-finding the tire. Edit's Back button goes to Details but a successful save goes to Index.

**Fix:** Redirect Create and Edit to the tire's Details page (with the 5.1 flash). Details is already the hub with Edit/Move actions, so nothing is lost.

### 5.3 `[ ]` The tire name in the inventory table is not a link

The only path to Details is the small "View" action on the far right; the row hover highlight *suggests* clickability that isn't there. Movements already links the tire cell — Index should too.

**Fix:** Wrap the Brand/Model cell (and SKU cell) in a Details link. With the tire cell clickable, "View" in row-actions becomes redundant and can be dropped, reducing the 2×2 action grid to a cleaner single row of three.

### 5.4 `[ ]` Scan box is invisible to anyone who doesn't already know it

`Views/Tires/Index.cshtml:38` — the scan input has a placeholder but no label and no button; submitting requires knowing to press Enter. It's also easy to miss that it exists at all.

**Fix:** Add a visually-hidden `<label>`, a small "Scan" submit button, and consider `autofocus` on the Index page so hardware barcode scanners (which type + Enter) work with zero clicks — with the caveat that autofocus steals keyboard focus from screen-reader users on every page load, so it may deserve a config toggle or `data-` opt-in.

### 5.5 `[ ]` Movement form ignores intent the UI already knows

Every path into RegisterMovement lands on the same form with `In` preselected. Coming from Low Stock, the intent is a delivery (`In`); a sale flow wants `Out`. Each wrong preselect is a mis-recorded ledger entry waiting to happen.

**Fix:** Accept an optional `type` query parameter in the GET action to preselect the movement type; make Low Stock's "Move" pass `type=In`. Optionally add explicit "Stock in / Stock out" links in row-actions or on Details.

### 5.6 `[ ]` No projected result on the movement form

The Adjustment/In/Out semantics differ (absolute vs. delta) and the hint text explains it, but the operator still does mental arithmetic. A dozen lines of vanilla JS can show "Resulting stock: 31" live under the quantity field, updating on type/quantity change — the strongest guard against the classic "entered the delta as an Adjustment" mistake. Degrades gracefully with JS off (hint text remains).

### 5.7 `[ ]` Delete page warns *after* the doomed attempt

`Views/Tires/Delete.cshtml` shows a generic warning that tires with movements can't be deleted, lets the user click the red button, and only then errors. The page has the tire ID; it can know upfront.

**Fix:** In the Delete GET, check for movements and render the blocked state directly: explain why, hide/disable the delete button, and point to Adjustment-to-zero (write-off) as the correct action for a tire with history.

### 5.8 `[ ]` Filter panel has no clear/reset inside it

The "Clear filter" link only appears in the results header after filtering. Inside the open filter panel, next to Search, there is no way to reset — users blank nine fields by hand.

**Fix:** Add a secondary "Clear" button (plain link to `Index`) beside Search in `filter-fields`.

### 5.9 `[ ]` Details movement history is unbounded

`GetTireAsync(includeMovements: true)` loads *all* movements; a busy SKU accumulates hundreds of rows on every Details view. The global journal can't rescue it because it has no per-tire filter.

**Fix:** Take the newest ~20 in the Include, show "View all N movements" linking to the journal filtered by tire — which means adding a `tireId` filter to `GetMovementsAsync` and `/Movements`. (The date-range filter already in `TODO.md` fits the same form.)

### 5.10 `[ ]` No favicon

There is no favicon at all; the browser tab shows a generic document icon next to Cyrillic titles. A single `favicon.svg` (black square, white "S", red index mark — matching the wordmark) plus `<link rel="icon">` in `_Layout.cshtml` finishes the brand.

### 5.11 `[ ]` No print styles

Warehouse reality: people print stock lists and the low-stock reorder sheet. Printing today emits the sticky topbar, filter chrome, red links, and useless action columns.

**Fix:** A short `@media print` block: hide `.topbar`, `.site-footer`, `.head-actions`, `.filter`, `.row-actions`, `.pager`; force black text, allow the table to use full width. Low Stock printed as a reorder sheet is the main win.

### 5.12 `[ ]` Default sort is invisible

The inventory opens sorted by brand, but no `▲` shows because the default sort key is `null`, not `"brand"`. Clicking "Tire" once appears to do nothing (it re-sorts to the same order). Treat `null` as `brand` when rendering the sort marker, or make the default explicit.

### 5.13 `[ ]` Accessibility pass

- `th` elements lack `scope="col"`; sortable headers lack `aria-sort`.
- Active nav tab and language link should carry `aria-current="page"` / `aria-current="true"` — the red bar and black chip are currently color/position-only signals.
- The scan input is placeholder-only (see 5.4).
- The Movements type filter links (`block-meta`) get an `is-active` class but no styling defines it there, so the active filter is invisible — style it (e.g. black underline or bold) and add `aria-current`.

### 5.14 `[ ]` Micro-polish

- Buttons and row-action links flip colors with no transition; a ~120ms `background-color/color` transition softens every interaction (reduced-motion media query already neutralizes it).
- "Delete" in row-actions looks identical to the safe actions until hover. Consider dropping Delete from the table entirely (it lives on Details) — fewer accidents, calmer rows, and with 5.3 the actions column shrinks to "Move / Edit".
- Table density: rows are generous (`--sp-2` padding both axes at 0.875rem type). Screenshots show the app being used at 80% browser zoom — a signal the data pages run large. Consider a compact table variant (reduce vertical padding to `--sp-1`) or trimming `--f-3` stat numerals on the data-heavy screens.
- The Report page's By Brand table could add a "% of value" column — the data is already in `ValueReportGroup`, and a share column makes the ranking meaningful at a glance.

---

## Suggested order of work

1. **1.1** (bg decimal entry — data entry is broken in the default culture), **1.2**, **1.3** — the three real bugs.
2. **5.1 + 5.2** (flash + redirect-to-details) — biggest UX return for the effort, touches the same controller code as 1.2.
3. **2.1–2.4** — hardening, each independent and small.
4. **1.4, 1.5, 3.1** — correctness edges.
5. **5.3–5.9, 5.12, 5.13** — UX round, mostly view-layer.
6. **3.2, 3.3, 4.x, 5.10, 5.11, 5.14** — housekeeping and polish.
