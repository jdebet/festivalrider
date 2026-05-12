# 019 — Show-scoped bands and running orders

## Status

Draft

## Context

Successor to [006-multi-show-support.md](./006-multi-show-support.md) and [018-export-save-pages.md](./018-export-save-pages.md). Plan 006 made running orders show-scoped but kept bands global, reasoning that the same band roster reuses across shows. Real-world usage shows that bands are almost always tied to a specific festival/show (different line-ups, different rider versions, different contacts). Keeping them global creates cross-contamination risk: editing a band for Show A leaks into Show B. This plan makes `ShowData` the true aggregate root: metadata, stages, bands, and running orders all live inside it. `AppState` becomes a thin container of shows plus an active-show pointer.

This also changes the bundle semantics: a normal bundle is a single show (self-contained), and a new "master bundle" acts as a container for multiple single-show bundles. This aligns export/import with the user's mental model of "I want to back up this festival" rather than "I want to back up the entire app state."

## Decisions (locked)

- **Show-scoped bands and running orders** — `ShowData` gains `List<Band> Bands` and `List<RunningOrder> RunningOrders`. `AppState` drops its top-level `Bands` and `RunningOrders`. Every show is a self-contained unit.
- **RunningOrder.ShowId stays** — It becomes a cached parent reference maintained by `BandService` (set when a running order is added to a show). Keeping it avoids rewriting every print strategy and running-order filter that currently dereferences `order.ShowId`.
- **Accessor scoping** — `IBandService.Bands`, `RunningOrders`, `RunningOrdersForActiveShow`, and the parameterless `FindStage` target the active show. Parameterless `FindBand(Guid)` and `FindRunningOrder(Guid)` scan ALL shows (so deep-links and print routes resolve regardless of active show). Show-scoped overloads `FindBand(Guid, Guid)` and `FindRunningOrder(Guid, Guid)` constrain lookup to one show; print strategies and bundle merges use these.
- **Schema bump** — `AppState.SchemaVersion` 4 → 5.
- **Implicit single stage** — When `ShowData.Stages` is empty, the app treats the show as having exactly one unnamed stage. `BandService.FindStage(showId, 0)` returns a synthetic `Stage { Id = 0, Name = "" }`. No UI requires stage name input. Running-order CSV export omits the `Stage` column; import maps empty/missing stage values to `StageId = 0`. Print output omits stage headers. Do NOT auto-create a `"Main"` stage in the model.
- **Single-show standard bundles** — `IBundleService.ExportBundle` takes a `ShowData`, not an `AppState`. The resulting `.zip` contains one show, that show's bands, and that show's running orders. `IBundleService.ImportBundle` imports into a target show (`Guid targetShowId`) within the current `AppState`.
- **Master bundle** — A second format (`festivalrider-master-bundle`) that contains nested single-show `.zip` files. `ExportMasterBundle(AppState)` and `ImportMasterBundle(Stream)` are new interface methods.
- **Bundle migration v4 → v5** — A v4 bundle is a multi-show full-AppState snapshot. The v4→v5 bundle migrator converts it into a v5 **single-show** bundle containing only the active show from the v4 manifest. Any non-active shows are dropped with a warning. Users who need all shows can re-export from the migrated app via master bundle.
- **Settings → Shows** — Route `/settings` becomes `/shows`. Page file stays `Shows.razor` (rename from `Settings.razor`). Nav menu updated. The "Manage Shows" card gains per-show export buttons.
- **Cross-show import** — Importing a single-show bundle always targets the current active show (or a user-selected show). Merge mode upserts bands and running orders scoped to that target show only; other shows in `currentState` are untouched.

## Open questions

None.

## Architecture rules

- `BandService` MUST be the sole mutator of `AppState` and every nested `ShowData` list. Other services MUST NOT mutate state directly.
- `BandService.AddBand` MUST set `Band.CreatedAt` and `Band.UpdatedAt`, add the band to the active show's `Bands` list, and raise `OnChange`.
- `BandService.DeleteBand` MUST remove the band from the active show's `Bands` and remove any `RunningOrderSlot` referencing that band across the **same show's** running orders only.
- `BandService.AddRunningOrder` MUST set `order.ShowId = ActiveShowId` before adding it to the active show's `RunningOrders`.
- `BandService.UpdateShow(ShowData show)` MUST copy only scalar metadata (`Name`, `Address`, `DateOfOpening`, `ShowDayCount`) and `Stages` into the existing show in `_state.Shows`. It MUST preserve the target show's existing `Bands` and `RunningOrders` lists. This prevents show-CSV import from silently wiping a show's roster.
- `BandService.ReplaceState` MUST run `EnsureShowInvariants` (seed a default show if the list is empty, fix `ActiveShowId`).
- `IBundleService.ExportBundle` MUST produce a `.zip` whose manifest has `"schemaVersion": 5` and lists exactly one show entry.
- `IBundleService.ExportMasterBundle` MUST produce a `.zip` whose manifest has `"format": "festivalrider-master-bundle"` and `"schemaVersion": 5`, listing nested single-show `.zip` entries under the `shows/` prefix.
- The implicit single-stage convention MUST be enforced in `BandService.FindStage` and in every UI and print strategy that renders stage information. NEVER auto-inject a `"Main"` stage into `ShowData.Stages`.
- Running-order CSV export MUST omit the `Stage` column when the show has no explicit stages. Import keeps the fixed `SlotRow` mapping (CsvHelper auto-maps missing columns to empty strings); the stage-resolution branch maps `r.Stage` to `StageId = 0` whenever `show.Stages.Count == 0`.
- Released localization keys MUST stay in `en.json` and `fr-fr.json` per the global AGENTS rule. New keys are added alongside; old `nav.settings` and `page.settings.*` keys remain present even if unreferenced.
- `LocalizationKeys.cs` keeps the existing `Page.Settings` nested class verbatim. A new sibling `Page.Shows` class is added for new constants. NEVER rename `Page.Settings`.
- Print strategies that resolve a band from a `RunningOrderSlot` MUST use the show-scoped overload `FindBand(order.ShowId, slot.BandId)`, not the active-show accessor. The same rule applies to `FindStage(order.ShowId, slot.StageId)`.
- `RiderPrint.razor` relies on strategy `GetTitle` throwing when the entity is missing; because `FindBand(Guid)` scans all shows, `BandRiderPrintStrategy.GetTitle` will locate any band regardless of active show. No change needed in `RiderPrint.razor` itself.
- Pages orchestrate; they inject services and pass data to components via `[Parameter]` / `EventCallback`. Components MUST NOT inject `IBandService`.

## File-by-file scope

### Models (`src/FestivalRider/Models`)

- `AppState.cs` — drop `List<Band> Bands` and `List<RunningOrder> RunningOrders`. Bump `SchemaVersion` default to 5. Constructor seeds one `ShowData` with empty `Bands` and `RunningOrders`.
- `ShowData.cs` — add `List<Band> Bands { get; set; } = new()` and `List<RunningOrder> RunningOrders { get; set; } = new()`.

### Services (`src/FestivalRider/Services`)

- `IBandService.cs` —
  - `Bands` and `RunningOrders` properties continue but now resolve against the active show.
  - Add `Band? FindBand(Guid showId, Guid id)` and `RunningOrder? FindRunningOrder(Guid showId, Guid id)`.
  - Parameterless `FindBand(Guid id)` and `FindRunningOrder(Guid id)` scan ALL shows (preserves deep links and print routes). `FindStage` stays active-show scoped (stages are resolved via their show context).
- `BandService.cs` —
  - Mutations (`AddBand`, `UpdateBand`, `DeleteBand`, `AddRunningOrder`, `UpdateRunningOrder`, `DeleteRunningOrder`) target the active show's nested lists.
  - `DeleteBand` only scrubs slots in the same show's running orders.
  - `FindBand(Guid id)` scans ALL shows and returns the first match. This preserves deep links and print routes for bands in non-active shows. `FindBand(Guid showId, Guid id)` scans the specified show only.
  - `FindRunningOrder(Guid id)` scans ALL shows' running orders and returns the first match. `FindRunningOrder(Guid showId, Guid id)` scans the specified show only.
  - `FindStage(Guid showId, int id)` returns a synthetic `Stage { Id = 0, Name = "" }` when `show.Stages.Count == 0 && id == 0`.
  - `EnsureShowInvariants` seeds a default show if `Shows.Count == 0`, with empty `Bands` and `RunningOrders`. It MUST also null-coalesce `show.Bands ??= new()` and `show.RunningOrders ??= new()` for every show so deserialized payloads with missing properties don't NRE.
  - `UpdateShow(ShowData show)` copies only scalar metadata (`Name`, `Address`, `DateOfOpening`, `ShowDayCount`) and the `Stages` list into the existing show in `_state.Shows`. It MUST preserve the target show's existing `Bands` and `RunningOrders` references unchanged.
- `IBundleService.cs` — replace `byte[] ExportBundle(AppState)` with `byte[] ExportBundle(ShowData show)`. Replace `BundleImportResult ImportBundle(Stream, BundleImportMode, AppState?)` with `BundleImportResult ImportBundle(Stream zip, Guid targetShowId, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null)`. Add `byte[] ExportMasterBundle(AppState state)` and `BundleImportResult ImportMasterBundle(Stream zip, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null)`.
- `BundleService.cs` —
  - `ExportBundle(ShowData)` writes `shows/{id}.csv`, `bands/*.csv`, `running-orders/*.csv` for the single show.
  - `ImportBundle` (single-show): in **Replace** mode, the incoming show's metadata, stages, bands, and running orders overwrite the target show entirely. In **Merge** mode, the target show's metadata and stages are preserved; only bands and running orders are upserted by `Guid`. `MergeInto` therefore operates on a single target show's `Bands` and `RunningOrders` only.
  - `ExportMasterBundle` creates nested single-show zips under `shows/*.zip`.
  - `ImportMasterBundle` reads the master manifest, iterates each nested zip, and assembles a complete `AppState` directly: for each incoming show, if `currentState` has a show with that `Id`, merge/replace into it; otherwise add it as a new entry in `state.Shows`. `BundleService` MUST NOT call `IBandService`; it returns the assembled `AppState` via `BundleImportResult` and the caller invokes `IBandService.ReplaceState`.
  - `Manifest` class updated for v5 shape (single show: `Show`, `Bands`, `RunningOrders`; no `ActiveShowId`). New `MasterManifest` class for master bundles.
  - `MergeInto` rewritten to operate on a single target show's bands and running orders.
- `IExportService.cs` — add `string ResolveBandName(Guid showId, Guid bandId)`. Keep existing `ResolveBandName(Guid bandId)` (active-show scoped).
- `ExportService.cs` —
  - `WriteSlotRows` accepts a `ShowData` parameter to determine whether to emit the `Stage` column. When `show.Stages.Count == 0`, register a CsvHelper class map that excludes `Stage` from the header; otherwise emit the full header.
  - `ExportRunningOrderCsv` and variants omit the `Stage` column when `show.Stages.Count == 0`.
  - `ImportRunningOrderCsv` keeps the fixed `SlotRow.GetRecords()` call. CsvHelper auto-maps the missing `Stage` column to `string.Empty`. The stage-resolution branch becomes: `var stageId = show.Stages.Count == 0 ? 0 : (stageByName.TryGetValue(r.Stage, out var sid) ? sid : 0);`. No hand-rolled header parsing.
  - `ResolveBandName(Guid)` uses active show. `ResolveBandName(Guid, Guid)` uses specified show.

### Migrators (`src/FestivalRider/Migrators`)

- `V4ToV5Migrator.cs` — new.
  - Reads v4 shape: top-level `bands` array, top-level `runningOrders` array, `shows` array. Treat missing/null `bands` or `runningOrders` as empty arrays.
  - Every nested `JsonNode` moved between containers MUST be `DeepClone()`-d first. `System.Text.Json.Nodes.JsonNode` rejects reparenting to a second parent and will throw `InvalidOperationException`. After cloning, the originals are dropped when the top-level arrays are removed.
  - Build a map `bandId -> first showId` by scanning every running order's slots in document order. A band referenced by slots in multiple shows is placed in the show whose `Id` appears **first in the `shows` array order**. The band JsonNode is cloned into exactly one show.
  - For each running order, clone it into the show whose `Id` matches `ro.ShowId`. If `ro.ShowId` is empty or doesn't match any show, append to the active show with a warning.
  - Unreferenced bands (no slot in any running order references them) are cloned into the active show (`activeShowId`). If `activeShowId` doesn't match any show, fall back to `shows[0]` with a warning.
  - For each show, ensure `bands` and `runningOrders` arrays exist (initialize to empty `JsonArray` if absent).
  - Remove top-level `bands` and `runningOrders` from the JSON root.
  - Stamp `schemaVersion = 5`.

### BundleMigrators (`src/FestivalRider/BundleMigrators`)

- `V4ToV5BundleMigrator.cs` — new.
  - Reads v4 manifest (multi-show, top-level `activeShowId`, separate `shows`/`bands`/`runningOrders` lists).
  - Selects the active show entry and drops all other show entries from the manifest and the `scratch.Entries` dictionary.
  - Drops running-order entries whose CSV `ShowId` column does NOT equal `activeShowId`.
  - KEEPS all band entries verbatim. Bands in v4 are global and referenced by `BandName` (string) in running-order CSVs, so the migrator cannot reliably determine which bands belong to the active show without ambiguity (two bands sharing a name). Extra bands in the resulting single-show bundle are harmless — they're surfaced to the user as part of the active show's roster on import, which matches the v4 → v5 state migration semantics.
  - Rewrites the manifest to v5 single-show shape (`show`, `bands`, `runningOrders`, no `activeShowId`).
  - Emits a warning if non-active shows were present in the v4 bundle.

### Pages / Components (`src/FestivalRider/Pages`, `src/FestivalRider/Components`)

- `Settings.razor` → rename to `Shows.razor`, change `@page "/settings"` to `@page "/shows"`.
  - "Manage Shows" card gains a per-show export button next to each show's actions.
  - Keep show management, show details, and danger zone.
  - ADD injections: `@inject IBundleService Bundle` and `@inject IJSRuntime JS` so the per-show export button can serialize and download. Reuse `Save.razor`'s `Sanitize` filename helper — extract it to a new static `FilenameSanitizer` class under `src/FestivalRider/Services/` so both pages share the implementation, OR duplicate the private method verbatim. Choose duplication if the helper is < 10 lines; extract if it grows.
- `NavMenu.razor` — change `href="settings"` to `href="shows"`. The `nav.settings` localization key STAYS in the catalogs (released-key rule); the link simply uses `nav.shows` now.
- `Save.razor` —
  - Status card: replace global band/running-order counts with active-show counts.
  - Standard bundle export: `Bundle.ExportBundle(activeShow)`.
  - Add master bundle export button: `Bundle.ExportMasterBundle(BandService.Snapshot())`.
  - Standard bundle import: pass `BandService.ActiveShowId` as `targetShowId`.
  - Add master bundle import section.
- `Export.razor` — change the stage-controls guard from `stagesUsed.Count > 0` to `_show.Stages.Count > 0 && stagesUsed.Count > 0`. With implicit stage, every slot has `StageId = 0` and `stagesUsed` would otherwise equal `{0}`, falsely rendering a useless "Stage 0" dropdown.
- `BandListV2.razor` — no structural changes; `BandService.Bands` resolves to active show.
- `RunningOrderV2.razor` —
  - Wrap the stage `<th>` in `@if (_show.Stages.Count > 0) { ... }`. The column is omitted entirely from the table (not merely hidden) so `<thead>` and `<tbody>` row widths stay aligned with `RunningOrderSlotRow`'s output.
  - `AddSlot` uses `StageId = 0` when there are no explicit stages.
  - Remove `_show.Stages.Count == 0` from the "Add slot" button's `disabled` condition; slots can always be added because the implicit stage exists.
  - Remove the "no stages" warning alert.
- `RiderEditorV2.razor` — no structural changes; it edits a band from the active show.
- `RiderPrint.razor` — no changes needed. Strategy `GetTitle` validation catches missing entities; because `FindBand(Guid)` scans all shows, `BandRiderPrintStrategy` resolves any band regardless of active show.

### Components (`src/FestivalRider/Components`)

- `RunningOrderSlotRow.razor` — wrap the stage `<td>` in `@if (Stages.Count > 0) { ... }`. The cell is omitted entirely from the row (not just disabled) so the row's `<td>` count matches `RunningOrderV2.razor`'s conditional `<th>` count. The slot's `StageId` stays `0` and is immutable from the UI. The component already receives `Stages` as a `[Parameter]`; no new injection.

### PrintStrategies (`src/FestivalRider/PrintStrategies`)

- `BandRiderPrintStrategy.cs` — uses `_bands.FindBand(id)`. Because `FindBand(Guid)` now scans all shows, it resolves bands in any show. No other changes needed.
- `StagePrintStrategy.cs` — in `BuildHeader`, omit the `<h1>` stage name when the show has no explicit stages (`stage.Name == ""`). `Resolve` already uses `FindStage(order.ShowId, ctx.StageId)`; no change there. In `BuildScheduleTable` and `BuildTechSummary`, switch `FindBand(s.BandId)` to `FindBand(order.ShowId, s.BandId)` (show-scoped).
- `RolePrintStrategy.cs` — when show has no explicit stages, the stage label in `BuildBandBlock` becomes empty (already falls back to `unknownStage` if stage is null; with implicit stage it will be a synthetic empty-name stage, so we need to treat empty name as implicit and skip rendering `"@ {stageLabel}"`). In `Resolve`, `FindRunningOrder` stays correct because it uses `Guid`. In `BuildBandBlock`, call `FindStage(order.ShowId, slot.StageId)` and `FindBand(order.ShowId, slot.BandId)`.

### Localization (`src/FestivalRider/wwwroot/i18n`, `LocalizationKeys.cs`)

- `en.json` — ADD the following keys. NEVER remove or rename existing keys (`nav.settings`, `page.settings.*`, etc. stay verbatim per global AGENTS rule):
  - `nav.shows` — new label for the renamed nav link.
  - `page.shows.title`, `page.shows.heading` — page title and heading for `/shows`.
  - `page.shows.exportShow` — per-show export button label.
  - `page.save.masterBundle.exportBtn`, `page.save.masterBundle.importBtn`, `page.save.masterBundle.description` — master bundle controls.
  - `toast.shows.bundleExported`, `toast.shows.masterBundleExported` — confirmation toasts.
- `fr-fr.json` — parity additions; same rule (no removals).
- `LocalizationKeys.cs` — KEEP `Page.Settings` static class verbatim. ADD a new sibling `Page.Shows` class for the new keys (`Title`, `Heading`, `ExportShow`). Same for `Toast.Shows` if a corresponding nested class doesn't already exist. `Save.razor` references for master bundle keys go under `Page.Save.MasterBundle`. The `Settings.razor` → `Shows.razor` page swap updates which constants are referenced; the constants themselves stay where they are.

### App bootstrap (`src/FestivalRider/Program.cs`)

- Register `V4ToV5Migrator` as `IStateMigrator`.
- Register `V4ToV5BundleMigrator` as `IBundleMigrator`.
- Ensure `V3ToV4Migrator` and `V3ToV4BundleMigrator` registrations stay intact.

### Tests (`tests/FestivalRider.Tests`)

- `BandServiceTests.cs` — update all tests to use show-scoped bands and running orders. Add tests for `FindBand(showId, id)`, `FindRunningOrder(showId, id)`, implicit stage lookup (`FindStage` with empty stages), and `DeleteBand` slot cleanup within the same show only.
- `BundleServiceTests.cs` — add single-show round-trip tests, master bundle round-trip tests, v4→v5 bundle migration tests (active-show selection, dropped-show warning).
- `ExportServiceTests.cs` — add tests verifying Stage column omission when show has no stages, and StageId=0 mapping on import.
- `Migrators/V4ToV5MigratorTests.cs` — new. Tests: band redistribution by show, unreferenced bands land in active show, running orders redistributed by `ShowId`.
- `BundleMigrators/V4ToV5BundleMigratorTests.cs` — new. Tests: active-show selection, manifest shape rewrite, dropped entries.
- `TestDataFactory.cs` — update `FullShow()` to seed empty `Bands` and `RunningOrders`. Update `FullRunningOrder` to accept a show and append to `show.RunningOrders`. Update `FullState()` helper in tests to nest bands/running orders inside shows. ADD a new `ShowWithNoStages()` factory (no stages, empty `Bands`/`RunningOrders`) for implicit-stage tests covering CSV column omission, `StageId = 0` mapping on import, and print header omission.

## Task order

Each step must leave the app compiling and runnable.

1. **Models** — Add `Bands` and `RunningOrders` to `ShowData`. Remove them from `AppState`. Bump `SchemaVersion` to 5. Fix constructor. Build green.
2. **BandService core** — Rewrite `BandService` to operate on active show's nested lists. Add show-scoped `FindBand`/`FindRunningOrder` overloads; make parameterless variants scan all shows. Implement implicit single stage in `FindStage`. Update `DeleteBand` to clean up same-show slots only. Update `UpdateShow` to preserve nested `Bands`/`RunningOrders`. Add null-coalescing in `EnsureShowInvariants`. Build green.
3. **Print strategies** — Update `StagePrintStrategy` and `RolePrintStrategy` to handle implicit stage (omit headers/labels when `stage.Name == ""`). Switch `FindBand(s.BandId)` to `FindBand(order.ShowId, s.BandId)` in both. Build green.
4. **ExportService implicit stage** — Update `ExportRunningOrderCsv` and variants to omit `Stage` column via a conditional CsvHelper class map when show has no stages. Update `ImportRunningOrderCsv` to map `r.Stage` to `StageId = 0` when `show.Stages.Count == 0` (no hand-rolled header parsing). Add `ResolveBandName(Guid showId, Guid bandId)`. Build green.
5. **RunningOrderV2 + RunningOrderSlotRow UI** — In BOTH files, wrap the stage `<th>` / `<td>` in `@if (Stages.Count > 0)` so column counts stay aligned. `AddSlot` uses `StageId = 0` when no explicit stages. Remove `_show.Stages.Count == 0` from the "Add slot" disabled condition. Remove the "no stages" warning. Update `Export.razor`'s stage-controls guard to `_show.Stages.Count > 0 && stagesUsed.Count > 0`. Build green.
6. **BundleService single-show** — Rewrite `ExportBundle(ShowData)`. Rewrite `ImportBundle` to target a `Guid targetShowId`. Update manifest to v5 single-show shape. Add `ExportMasterBundle` / `ImportMasterBundle`; the latter assembles a complete `AppState` directly without calling `IBandService`. Build green.
7. **Save page** — Update status counts, wire single-show bundle export, add master bundle export/import, wire single-show import with `ActiveShowId` as target. Build green.
8. **Shows page (rename)** — Rename `Settings.razor` to `Shows.razor`, change route `/settings` → `/shows`, update nav `href`. Inject `IBundleService` and `IJSRuntime`. Add per-show export buttons reusing `Save.razor`'s `Sanitize` filename helper. Build green.
9. **State migrator** — Write `V4ToV5Migrator` with `JsonNode.DeepClone()` when moving bands and running orders into shows. Handle missing top-level arrays, orphan `ShowId` mismatches, and ensure each show has `bands`/`runningOrders` arrays. Register in `Program.cs`. Build green.
10. **Bundle migrator** — Write `V4ToV5BundleMigrator` that drops non-active show entries and non-active running-order entries but keeps all band entries verbatim. Rewrite manifest to v5 single-show shape. Register in `Program.cs`. Build green.
11. **Localization** — ADD new keys to `en.json`, `fr-fr.json`, and `LocalizationKeys.cs`. NEVER remove `nav.settings`, `page.settings.*`, or rename `LocalizationKeys.Page.Settings`. Add sibling `Page.Shows`, `Toast.Shows`, `Page.Save.MasterBundle` classes. Build green.
12. **Tests** — Update `BandServiceTests`, `BundleServiceTests`, `ExportServiceTests`. Write `V4ToV5MigratorTests`, `V4ToV5BundleMigratorTests`. Build green.
13. **Parity & verify** — Run full test suite. Verify `LocalizationCatalogTests` passes.

## Implementation cadence

- **Wave 1 — Domain & service (steps 1–5)** — Models, `BandService`, print strategies, `ExportService`, and `RunningOrderV2` implicit-stage handling. Demoable: app runs, single-show UX intact, implicit stage works.
- **Wave 2 — Bundle & persistence (steps 6–7)** — Single-show and master bundle export/import, `Save` page wired. Demoable: export a show, import it into another show, export master bundle.
- **Wave 3 — UI & routing (steps 8–9)** — `/shows` rename, per-show export buttons, state migrator live. Demoable: v4 data migrates on first load, new route works.
- **Wave 4 — Bundle migration & localization (steps 10–11)** — v4 bundle import works, all strings localized.
- **Wave 5 — Tests (steps 12–13)** — Full green suite.

## Out of scope

- Live cross-tab sync (already out of scope per AGENTS.md).
- Per-show locale (locale stays global).
- Copying a running order or band between shows via UI (manual bundle round-trip only).
- Downgrading bundles from v5 to v4.

## Risks & migrations

- **v4 state data loss** — The `V4ToV5Migrator` redistributes bands into shows. If a band is referenced by running orders in *multiple* shows (possible in v4 because bands were global), the migrator puts the band in the show of the *first* running order that references it. The band object is not duplicated. This is acceptable because cross-show band sharing was an edge case in 006 and is explicitly removed by this plan.
- **v4 bundle multi-show data loss** — The v4→v5 bundle migrator keeps only the active show. Users with multi-show v4 bundles will lose non-active shows on import. Mitigation: they can open the v4 bundle in a pre-019 build, set each show active, and export individual single-show bundles.
- **Implicit stage breaking existing running orders** — v4 running orders that reference a stage ID (e.g., `StageId = 1` for "Main") will still work if the show has explicit stages. If the show has NO explicit stages, those slots will resolve to `Unknown stage` until stages are added. The migrator does not auto-add stages.
- **Implicit → explicit stage transition** — When a show starts with no stages, slots use `StageId = 0` (implicit). If the user later adds the first explicit stage (e.g., `Id = 1`, "Main"), all existing `StageId = 0` slots now resolve to `Unknown stage` because the implicit stage convention only applies when `Stages.Count == 0`. There is NO auto-remap; the user must manually reassign slots or delete and recreate the running order. This is by design: silently remapping `0 → 1` would be a hidden mutation that violates the "no auto-inject" rule.
- **Deep links to `/settings`** — Bookmarked `/settings` links will 404 after the rename. The app is a Blazor WASM SPA with no server-side routing beyond `404.html`; we do not add redirects.
