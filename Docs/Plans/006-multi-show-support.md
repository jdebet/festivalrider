# 006 — Multi-show support

## Status

`Superseded by 019 (partial)`

## Context

Successor to [002-domain-model-revision.md](./002-domain-model-revision.md), which locked a single `ShowData` root and listed multi-show as out of scope. Real-world usage now needs one workspace to hold several festivals concurrently (e.g. a touring booker maintaining a roster across summer events, or a stage crew rehearsing two weekends back-to-back). This plan generalizes `AppState` to a list of shows with an active-show pointer, scopes running orders by show, and keeps bands global so the same band roster reuses across shows. Schema bumps 2 → 3.

## Decisions (locked)

- **Shows collection** — `AppState.Shows : List<ShowData>`. Single-show installs migrate to a one-element list. Empty list is invalid; the app seeds a default "Untitled show" when the last show is deleted.
- **Show identity** — `ShowData` gains `Guid Id` (default-constructs to `Guid.NewGuid()`). All other `ShowData` fields keep 002's shape.
- **Active show pointer** — `AppState.ActiveShowId : Guid`. Always equals the `Id` of one element in `Shows`. Mutators (`AddShow`, `DeleteShow`) maintain this invariant: deleting the active show flips `ActiveShowId` to the next-by-creation-order survivor; deleting the last show seeds a fresh one and points to it.
- **Band scope** — `Band` stays global (no `ShowId`). The same band can appear in multiple shows' running orders.
- **Running order scope** — `RunningOrder` gains `Guid ShowId`. Pages filter running orders by `ActiveShowId` for default views; bundle export and CSV export include `ShowId` so cross-show round-trips are unambiguous.
- **Stage scope** — stages remain inside `ShowData.Stages` (per-show). `Stage.Id` uniqueness is per-show, not global; `RunningOrderSlot.StageId` resolves against its `RunningOrder.ShowId`'s stage list.
- **Schema bump** — `AppState.SchemaVersion` 2 → 3. v2 payloads MUST migrate via the framework introduced in [008-schema-migration.md](./008-schema-migration.md); 008's v2→v3 migrator wraps the existing single `ShowData` into `Shows = [it]`, sets `ActiveShowId = it.Id`, and copies any pre-existing `RunningOrder` items with `ShowId = it.Id`.
- **Bundle format** — manifest gains `shows : string[]` (replacing single `show : string`). Bundle CSV layout: `shows/{Guid}.csv`. v2 bundles are not auto-upgraded; import refuses with a "regenerate from v3" toast (the 003 schema-mismatch policy still wins).
- **Service surface** — new `IBandService` methods: `Guid AddShow(string name) -> Guid`, `Task UpdateShow(ShowData)`, `Task DeleteShow(Guid id)`, `Task SetActiveShow(Guid id)`. Existing `AddStage`/`UpdateStage`/`DeleteStage` operate on the active show by default; overload variants accept an explicit `Guid showId`.
- **UI surface** — `NavMenu` gains a show-picker dropdown bound to `Shows` + `ActiveShowId`. `Settings.razor` gains a "Manage shows" panel (add / rename / delete / set active). `RunningOrder.razor` filters its list to the active show.
- **No cross-show clone** — copying a running order between shows is out of scope (manual recreate or bundle export/import only).
- **Print routes** — print strategies that depend on a `RunningOrder` keep their existing `Guid` context (the running order self-identifies its show via `ShowId`). No URL change.

## Open questions

None.

## Architecture rules

Inherits 001 / 002 / 003 / 005 unchanged. Additional:

- A running order MUST always reference an existing `Show.Id`. Orphaned running orders MUST surface in the UI as "Unknown show" and are excluded from print until reassigned or deleted.
- `IBandService` MUST be the only mutator of `AppState.Shows` and `AppState.ActiveShowId`. UI binds via service methods, never via direct list mutation.
- `BundleService` (per 003 + 005) stays pure: multi-show export/import operates on `AppState` snapshots passed in by the caller.

## File-by-file scope

### Models (`src/FestivalRider/Models`)

- `AppState.cs` — replace `ShowData ShowData` with `List<ShowData> Shows` + `Guid ActiveShowId`. Bump default `SchemaVersion` to 3.
- `ShowData.cs` — add `Guid Id` (default `Guid.NewGuid()`).
- `RunningOrder.cs` — add `Guid ShowId`. Existing `ShowDayNumber` semantics unchanged (resolved against the referenced show's `DateOfOpening`).

### Services (`src/FestivalRider/Services`)

- `IBandService.cs` — add `AddShow`, `UpdateShow`, `DeleteShow`, `SetActiveShow`; add `IEnumerable<RunningOrder> RunningOrdersForActiveShow` accessor or equivalent helper. Stage methods gain `Guid showId` overloads.
- `BandService.cs` — implement the show CRUD with the active-show invariant (no empty `Shows`, valid `ActiveShowId` always). Raises `OnChange` per 001 rules.
- `BundleService.cs` — manifest schema updated to v3; export iterates `Shows` and writes one CSV per show; import composes `Shows` list and validates `ActiveShowId`. Replace and Merge (per 005) both updated. Stage-name remap on Merge becomes per-show: incoming `(ShowId, StageId)` remaps to local `(MatchingShowId, MatchingStageId)` matching by `ShowData.Name` then `Stage.Name`. Unmatched show name → entire running order skipped with warning.

### Pages / Components (`src/FestivalRider/Pages`, `src/FestivalRider/Components`)

- `Layout/NavMenu.razor` — show-picker dropdown bound to `IBandService.Shows` + `ActiveShowId`.
- `Pages/Settings.razor` — "Manage shows" panel (add / rename / delete / set active). Existing "Show details" controls retarget the active show.
- `Pages/RunningOrder.razor` — filter list to active show; new running orders default `ShowId = ActiveShowId`.
- `Components/ShowPicker.razor` — new component. `[Parameter] IReadOnlyList<ShowData> Shows`, `[Parameter] Guid ActiveShowId`, `[Parameter] EventCallback<Guid> OnChange`. No service injection (per layering rule).

### Tests (`tests/FestivalRider.Tests`)

- `BandServiceTests.cs` — show CRUD, active-show invariant maintenance, stage scoping.
- `BundleServiceTests.cs` — multi-show export/import round-trip; merge stage-name remap honoring per-show scoping; v2 bundle rejection.
- `ExportServiceTests.cs` — `RunningOrder` CSV gains `ShowId` column; per-show CSV export verified.

### Docs (`Docs/Plans`)

- `002-domain-model-revision.md` — flip `## Status` to `Superseded by 006 (partial)`. Only the "single ShowData root" decision and the "multi-show out of scope" item are superseded; section list, removals, and CSV layout from 002 still stand.
- `003-bundle-zip-export-import.md` — already `Superseded by 005 (partial)`; 006 amends manifest format. Append a one-line successor pointer in 005 § Risks (or leave to readme index).
- `readme.md` — index gets a row for 006 and updated status for 002.

## Task order

Each step leaves the app compiling and runnable. Steps are grouped impl → revisit → tests → docs.

### Implementation

1. **Models + service surface, no UI.** Add `Guid Id` to `ShowData`, swap `AppState.ShowData` for `List<ShowData> Shows` + `Guid ActiveShowId`, add `RunningOrder.ShowId`. Bump `SchemaVersion` to 3. Implement show CRUD in `BandService` with the active-show invariant. Existing pages bind to `Shows.Single(s => s.Id == ActiveShowId)` to keep current behavior pixel-identical. Build green, no UI change.
2. **Running-order scoping.** Wire `RunningOrder.ShowId` on add. Filter `RunningOrder.razor` by `ActiveShowId`. Update `BundleService` export/import to honor `Shows[]` and per-show stage remap on Merge.
3. **UI surface.** Add `ShowPicker.razor`, mount in `NavMenu`. Add "Manage shows" in `Settings.razor`. Manual e2e: create a second show, switch, add running orders scoped to it.

### Revisit checkpoint

4. **Triage post-impl.** Run the full suite + manual smoke. If steps 1–3 surface bugs (e.g. orphan handling edge cases, stage remap failure modes, picker UX ambiguity), update **Decisions (locked)** here only via amendment to this plan or a successor; do NOT expand silently into the test step.

### Tests

5. **Service tests.** `BandServiceTests` for show CRUD + active-show invariant + stage scoping.
6. **Bundle tests.** `BundleServiceTests` for multi-show round-trip, per-show stage remap on Merge, v2 bundle rejection.
7. **Export tests.** `ExportServiceTests` for the new `RunningOrder` CSV column.

### Docs

8. **Successor bookkeeping.** Flip 002 to `Superseded by 006 (partial)`; update `Docs/Plans/readme.md` index. Update relevant `AGENTS.md` files if any rule referenced single-`ShowData` semantics.

## Implementation cadence

- **Wave 6a — Domain & service (steps 1–2).** Demoable: existing single-show UX intact, internals multi-show.
- **Wave 6b — UI (step 3).** Demoable: actual multi-show usage end-to-end.
- **Wave 6c — Verification (step 4).** Reviewable as a no-op or as a plan amendment.
- **Wave 6d — Tests (steps 5–7).** Reviewable as a self-contained green suite expansion.
- **Wave 6e — Docs (step 8).** Index + status sync.

## Out of scope

- **Cross-show running-order clone.** Manual recreate or bundle round-trip only.
- **Per-show band roster.** Bands stay global; a "shows this band has played" view is a future plan.
- **Per-show settings (theme, currency, locale).** Out of scope; `ShowData` stays minimal.
- **Backwards-compat bundle import (v2).** Hard refused; bundle authors regenerate from v3.

## Risks & migrations

- **Schema 2 → 3 migration.** Depends on [008-schema-migration.md](./008-schema-migration.md). If 008 has not landed when 006 ships, fall back to 001's backup-and-reset policy and surface a "v2 data archived" toast.
- **Running orders pinned to deleted shows.** Mitigation: `DeleteShow` cascades to its running orders with a confirm dialog summarizing the count.
- **Stage-id collision across shows on Merge.** Per-show stage scoping makes name remap show-aware; an unmatched show name fails the running order whole, mirroring 005's all-or-nothing rule.
- **NavMenu real estate.** Picker is a dropdown, not a tab strip; collapses gracefully on narrow viewports.
- **Active-show invariant drift.** Single mutator (`BandService`) + invariant assertion in `ReplaceState` catches malformed snapshots from bundle imports.
