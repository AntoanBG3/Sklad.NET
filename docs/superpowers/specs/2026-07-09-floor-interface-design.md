# A mobile interface for the warehouse floor

## Context

`TODO.md` lists "a mobile interface for working on the warehouse floor" among
its future ideas. Read as a layout task it is largely already done, and doing it
again would be wasted work.

`wwwroot/css/site.css` carries breakpoints at 1360px, 900px, 760px and 480px.
The topbar drops to its own horizontally scrollable row below 1360px. Touch
targets are already `2.75rem` under that breakpoint, and a `@media (pointer:
coarse)` rule lifts the scan box's font to `1rem` specifically so iOS Safari
does not zoom into it. The pages are responsive.

What is not right is the **number of taps**. A picker holding a phone at the
rack, wanting to book five tires out, walks:

1. `/Tires` — the inventory index, with its filter panel, sort controls and
   pager, to reach the scan box.
2. `/Tires/Scan?code=…` — which redirects to
3. `/Tires/Details/{id}` — a reading page carrying a movement timeline, a stock
   value block and admin actions, none of which a picker wants.
4. `/Tires/RegisterMovement/{id}` — the form.
5. Back to Details.

Five loads, on a phone, to record one movement. That is the problem.

Scope, decided during design: **a focused scan-and-book flow** at its own route,
with an **autofocused text input** (no camera), showing **stock and location**
prominently, offering **In and Out only**.

## Facts that shape the work

Each verified against source.

1. **The lookup already works for this.** `InventoryService.FindByCodeAsync`
   (`InventoryService.cs:115`) matches `Sku` **or** `Barcode`. Both columns
   carry a `NOCASE` collation, so case-insensitive and Cyrillic-insensitive
   matching is already solved. The floor screen adds no lookup logic.

2. **The ledger already does everything booking needs.**
   `RegisterMovementAsync` (`InventoryService.cs:220`) validates the quantity,
   guards insufficient stock, stamps `UserName`, retries up to three times on a
   concurrency conflict, and **returns the new quantity**. The floor screen adds
   no stock logic. This is the single most important constraint on this work: if
   the implementation grows a second path that writes `Tire.Quantity`, it is
   wrong.

3. **`Adjustment` is a live hazard on a form post.** `MovementType` is an enum
   bound from the request. `Adjustment` sets stock *absolutely* rather than
   adding to it, and `RegisterMovementAsync` explicitly permits a quantity of
   zero for it (`InventoryService.cs:222-225`, allowing write-off). So a crafted
   `POST /Floor/Book` with `MovementType=Adjustment&Quantity=0` would silently
   zero a tire. Hiding the option in the view is not a control. The controller
   must reject anything that is not `In` or `Out`, and a test must prove it.

4. **`InsufficientStockException` carries the numbers.** It exposes `Available`
   and `Requested` (`InventoryExceptions.cs:31-42`), so the floor screen can say
   "only 3 left" rather than "error".

5. **Auth needs no work.** Authorization is a global `AuthorizeFilter`, so
   `/Floor` requires sign-in the moment it exists. Movements attribute
   themselves to the signed-in user already. No new role, no new policy.

6. **Layout is chosen per view.** `Views/_ViewStart.cshtml` sets `Layout =
   "_Layout"` for everything. A `Views/Floor/_ViewStart.cshtml` overrides it for
   this area alone, which is why a separate layout costs one file rather than a
   refactor.

7. **The flash convention is `TempData["Flash"]`**, rendered by `_Layout`
   (`_Layout.cshtml:63`). `_FloorLayout` must render it too, or a successful
   booking would confirm nothing.

## Routes

A new `FloorController`. Three actions, two screens.

- `GET /Floor` — the scan screen. One autofocused input, submitting `GET` to
  `Floor/Tire`.
- `GET /Floor/Tire?code=…` — resolve through `FindByCodeAsync`.
  - Found: render the booking screen.
  - Not found: re-render the **scan screen** with a message, HTTP 200, focus
    retained. It must not redirect to `/Tires` the way `/Tires/Scan` does; on
    the floor, losing the screen loses the worker's place.
  - Blank or missing code: redirect to `GET /Floor`.
- `POST /Floor/Book` — book the movement, then redirect to `GET /Floor` with a
  flash naming the tire and its new stock, so the worker's hands return to the
  next barcode.

`/Tires/Scan` is **unchanged**. Office staff keep landing on Details.

## The booking screen

Above the fold, in this order: the tire's SKU and description, its **location**,
its **current quantity**. Then a quantity stepper defaulting to 1, then two
large buttons, In and Out.

The stepper is a real `<input type="number" min="1">` with `−` and `+` buttons
layered on by script. If the script fails, the input still works and the form
still submits. Nothing on this screen requires JavaScript.

## Booking rules

```
POST /Floor/Book  { TireId, MovementType, Quantity }
```

- `MovementType` **must** be `In` or `Out`. Anything else — `Adjustment`, an
  out-of-range integer — is rejected before the service is called (fact 3).
- Delegate to `RegisterMovementAsync(tireId, type, quantity, note: null,
  userName: CurrentUser)`, mirroring `TiresController.cs:24`, which exposes
  `private string? CurrentUser => User.Identity?.Name;`. Pass no note; the floor
  is not the place to type one.
- `InsufficientStockException` → re-render the booking screen with a localized
  message quoting `Available` (fact 4). Stock must be unchanged.
- `InvalidMovementQuantityException` → re-render with a quantity error.
- `TireNotFoundException` → back to `GET /Floor` with a message. A tire can be
  deleted between the scan and the tap.
- Success → `RedirectToAction(nameof(Index))` with
  `TempData["Flash"]` naming the tire and the new quantity that
  `RegisterMovementAsync` returned.

Nothing in this controller touches `SkladDbContext`. All of it goes through
`IInventoryService`, per the project's architecture rule that controllers only
orchestrate.

## Layout and styling

`Views/Shared/_FloorLayout.cshtml`: the same `<meta name="viewport">` and
`site.css` as the main layout, the flash region, a thin header carrying the app
name and a link back to the full application, and nothing else. No tab row, no
filters, no footer.

New `.floor-*` classes appended to `site.css`. They inherit the existing tokens
and hold the four-colour discipline: `--black`, `--white`, `--gray`, `--red`.
Red stays reserved for danger, which here means the insufficient-stock message,
not the Out button. Out is a tint, as on the charts.

## Localization

Every new string gets a `<data name>` entry in `Resources/SharedResource.bg.resx`.
`LocalizationTests.Resx_covers_every_localized_key` scans Views, Controllers and
Services and fails on any miss, so an untranslated string turns the suite red
rather than silently rendering English under `bg-BG`.

## Testing

No migration, no model change, no new service, so no schema risk.

`Sklad.Tests/FloorControllerTests.cs`, using the real `InventoryService` with
`NullLogger`, plus `FakeLocalizer` per the project's controller-test convention:

- An unknown code re-renders the scan view with a message and returns a
  `ViewResult` — explicitly **not** a `RedirectToActionResult`.
- A blank code redirects to `Index`.
- A known SKU returns the booking view model. So does a known barcode, in a
  different case, proving the `NOCASE` path is the one being used.
- `POST` an `In` of 3 raises the tire's quantity by 3 and writes one
  `StockMovement` attributed to the signed-in user.
- `POST` an `Out` beyond stock re-renders the booking screen, and the tire's
  quantity is **unchanged**.
- **`POST` with `MovementType=Adjustment` is rejected and writes no movement,**
  and the tire's quantity is unchanged. This is the test that protects fact 3.
  It must assert on the database, not merely on the action result.
- A `POST` for a deleted tire redirects to `Index` rather than throwing.

## Verification

`dotnet test Sklad.Tests/Sklad.Tests.csproj` green after every commit. CI builds
with `--warnaserror`.

Because Razor views compile at build, the suite proves the views compile; it does
not prove they render. So, against the running app with
`ASPNETCORE_ENVIRONMENT=Development`:

1. `/Floor` renders with no topbar and the input focused.
2. Scanning a known SKU shows location and stock above the fold.
3. Booking an Out of 1 returns to `/Floor` with a flash and the new stock, and
   `/Movements` shows the movement attributed to `admin`.
4. An Out beyond stock shows the "only N left" message and leaves stock alone.
5. A hand-rolled `POST /Floor/Book` with `MovementType=Adjustment` changes
   nothing.
6. Under `bg-BG` every label is Bulgarian.
7. Rendered at a 390px viewport in headless Chrome, the buttons are full-width
   and nothing overflows horizontally.

## Out of scope, deliberately

Adjustment from the floor, camera scanning, offline/PWA support, and a
stock-taking mode. Each was considered and set aside; the first is a hazard, the
rest are their own projects.

## Commits

1. `feat(floor): add the floor scan screen and slim layout`
2. `feat(floor): resolve a scanned code to a booking screen`
3. `feat(floor): book In and Out movements from the floor`
4. `docs: record the warehouse floor interface`

No `Co-Authored-By` trailer.
