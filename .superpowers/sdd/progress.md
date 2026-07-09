# Charted reports — progress ledger

Plan: docs/superpowers/plans/2026-07-09-charted-reports.md
Branch: feature/charted-reports
Base: d6f7f34

Task 1: complete (commits 1e98ef2..0797d3c, review clean, 158 tests)
  Minor (deferred to final review): Trend.Label has no test for the Day
  branch ("d MMM"); only the Month branch is asserted.
Task 2: complete (commits 0797d3c..f9842c9, review clean, 164 tests)
Task 3: complete (commits f9842c9..4626ce6, review clean, 168 tests)
  Plan defect found: view uses CSS class `filter-inline`, undefined in
  site.css. Task 5 edits site.css and must define it.
Task 4: complete (commits 4626ce6..4575772, review clean, 169 tests)
  Minor (deferred to final review): Chart_js_is_vendored_and_tracked asserts
  File.Exists only; a truncated/HTML re-download would still pass.
Task 5: complete (commits 4575772..75d1623, review clean, 169 tests)
  Implementer caught a real spec violation: .filter-inline printed. Fixed
  in 75d1623. Step 7 (browser verification) reserved for the controller.

Step 7 browser verification (controller, against the running app) — ALL PASS:
  - /Tires/Report 200; three canvases, all role="img"; chart.js served from
    /lib/chart.js/dist/chart.umd.js; no CDN reference anywhere on the page.
  - JSON island is raw (no &quot;), parses, 12 monthly buckets by default,
    confirming the AddMonths(-11) inclusive-endpoint fix.
  - bg-BG default: month labels "авг 2025".."юли 2026", series "Входящо"/
    "Изходящо", seasons "Лятна/Зимна/Всесезонна". en-GB: "In"/"Out"/"Aug 2025".
  - CENTRAL CLAIM: registering an Adjustment(42) moved NEITHER bar
    (In=831 Out=1378 before and after) and incremented corrections 82 -> 83.
    A following Out(5) moved only the Out series, 1378 -> 1383.
  - Granularity: ?from=2026-06-10&to=2026-07-09 yields 30 daily buckets
    ("10 юни".."9 юли").
  - Validation: from>to renders the localized bg error and falls back to 12
    buckets; a 26-year span renders its own error and falls back. No 500s.
  - SECURITY: created a tire whose Brand is literally `</script><h1>pwned`.
    The island contains no literal `<`, still parses, the brand round-trips
    intact, and no <h1>pwned appears in the page. Breakout vector is closed.
  Dev-db artifacts left by this run: tire SKU XSS-1 (pending delete), one
  Adjustment and one Out movement noted "sdd-check" on tire 68.
Task 6: complete (commit 58cc62a, 169 tests, docs)
  Plan defect: it told Task 6 to `git add CLAUDE.md`, but CLAUDE.md is
  gitignored (.gitignore:10, commit b5fa111). Edited locally, not staged.

Final review: the dispatched reviewer hit the session limit mid-run. Its named
risk points were checked directly instead:
  - TrendBucket.StartUtc was dead (never read) -> removed (2cdd595).
  - No duplicate resx keys; every key added for this feature is used.
  - MaxSpanDays=3660 forces monthly granularity, so ~121 buckets max. Bounded.
  - DST: added a winter-offset test, mutation-verified (4437efa).
  - Both deferred Minors fixed in 2cdd595.
Render check (headless Chrome, JS executed): all three charts paint; bg labels;
black-vs-tint series. Found and fixed a locale bug no test could see: Chart.js
printed en-US axis ticks ("25,000") next to the page's "140 750 €".
Dev-db: XSS-1 test tire could NOT be deleted -- it has an opening Adjustment
movement and the delete guard correctly blocks it. Harmless, gitignored.
