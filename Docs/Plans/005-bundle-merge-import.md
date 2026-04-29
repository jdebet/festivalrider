# 005 — Bundle merge-on-import

## Status

`Active`

## Context

Successor to [003-bundle-zip-export-import.md](./003-bundle-zip-export-import.md), which shipped Replace-only import and explicitly deferred Merge. Production usage now needs two engineers (e.g. monitor + FOH) to round-trip their slice of the bands list without nuking each other's edits. This plan adds a Merge mode that upserts `Band` and `RunningOrder` by `Guid`, preserves locally unlisted entities, and remaps stage references by name. The bundle wire format from 003 (manifest + per-entity CSVs + 20 MB cap + schema-version gate) is unchanged.

## Decisions (locked)

- **Mode selection** — `enum BundleImportMode { Replace, Merge }`. `Replace` keeps 003's exact semantics. `Merge` is opt-in per import; default stays `Replace` to preserve existing call sites.
- **Service signature** — `IBundleService.ImportBundle(Stream zip, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null)`. `currentState` is required for `Merge` and ignored for `Replace`. Service stays pure (no `IBandService` dependency) per 003's architecture rule.
- **Upsert key** — `Guid` for both `Band` and `RunningOrder`. Existing entity replaced wholesale on hit. Locally present entities not listed in the bundle are preserved.
- **Conflict policy — bands** — last-write-wins by `Band.UpdatedAt`. If `incoming.UpdatedAt > existing.UpdatedAt`, replace; otherwise skip with a warning. Equal timestamps treated as skip (deterministic, no churn).
- **Conflict policy — running orders** — `RunningOrder` has no `UpdatedAt`, so incoming always wins on `Guid` collision; the displaced order surfaces as a warning so the user can re-export their copy if needed.
- **ShowData on Merge** — never modified. The festival-level root (`Name`, `Address`, `DateOfOpening`, `ShowDayCount`, `Stages`) belongs to the recipient; senders cannot mutate it via Merge. Replace mode is the only path that overwrites `ShowData`.
- **Stage remap on Merge** — each incoming `RunningOrderSlot.StageId` is remapped to the local `Stage.Id` whose `Name` matches case-insensitively (trimmed). Unmatched stage name → the entire enclosing `RunningOrder` is skipped with a warning naming the missing stage(s). Partial slot drops are NOT allowed; a running order is all-or-nothing.
- **Schema-version mismatch** — same hard refusal as Replace (003): no migration, toast + log, no partial apply.
- **Bundle size cap** — unchanged at 20 MB.
- **Result shape** — `BundleImportResult` extended with non-breaking optional `MergeStats? Merge` field. `Replace` leaves it `null`. `MergeStats` carries `BandsAdded`, `BandsUpdated`, `BandsSkipped`, `RunningOrdersAdded`, `RunningOrdersUpdated`, `RunningOrdersSkipped`. Top-level `BandCount` / `RunningOrderCount` continue to mean "successfully applied" (added + updated) for both modes.
- **Persistence dispatch** — Settings still calls `IBandService.ReplaceState(result.State!)` exactly once on success, regardless of mode. The merged `AppState` is composed entirely inside `BundleService`; no per-entity dispatch.
- **Confirm dialog wording** — Replace keeps 003 wording. Merge uses: `"Merge {N} band(s) and {M} running order(s) into the current data? Existing entries with newer timestamps will be kept."` Confirmation is mandatory in both modes.
- **UI default** — Settings defaults the radio to `Replace` on every page load (no persisted preference). Mode is read at the moment Import is confirmed, not when the file is staged.

## Open questions

None.

## Architecture rules

Inherits 001 / 002 / 003 unchanged. Additional:

- `BundleService` MUST remain pure: no `IBandService`, no `IStorageService`, no `IJSRuntime`. Merge consumes `currentState` as an argument and returns a fully-formed `AppState`.
- Merge MUST be deterministic: identical `(currentState, bundle)` inputs produce byte-identical merged state. Sort merged `Bands` and `RunningOrders` by `Guid` ascending after merge.
- Merge MUST NOT mutate `currentState`. Treat it as immutable input; clone what you keep.
- Stage remap MUST be name-based, never id-based. Stage IDs are local-only identifiers and are never portable across installs.
- A skipped running order MUST NOT leave any of its slots in the merged state.

## File-by-file scope

### Services (`src/FestivalRider/Services`)

- `BundleImportMode.cs` — new enum `{ Replace, Merge }`.
- `BundleImportResult.cs` — add `MergeStats? Merge` field (nullable, defaults `null`). Existing positional members unchanged so callers keep compiling.
- `MergeStats.cs` — new record `MergeStats(int BandsAdded, int BandsUpdated, int BandsSkipped, int RunningOrdersAdded, int RunningOrdersUpdated, int RunningOrdersSkipped)`.
- `IBundleService.cs` — extend `ImportBundle` signature with optional `BundleImportMode mode` and optional `AppState? currentState`. `ExportBundle` unchanged.
- `BundleService.cs` — split internal flow: shared manifest + entity decode produces an intermediate `(ShowData show, IReadOnlyList<Band> bands, IReadOnlyList<RunningOrder> orders)` plus warnings; `Replace` composes a fresh `AppState` (003 behavior); `Merge` calls a new `MergeInto(currentState, …)` helper that performs upsert + stage remap + sorting and accumulates `MergeStats`.

### Pages (`src/FestivalRider/Pages`)

- `Settings.razor` — Bundle section gains a mode radio (Replace / Merge) above the Import button. `StageBundleImport` passes the selected mode plus `BandService.Snapshot()` into `ImportBundle`. Confirm dialog message switches on mode. Success toast for Merge formats `MergeStats` (e.g. `"Merge applied: +2 bands, ~1 updated, 1 skipped; +1 running orders."`); Replace keeps current toast.

### Tests (`tests/FestivalRider.Tests`)

- `BundleServiceTests.cs` — extend with merge cases: upsert by Guid (add + replace + preserve unlisted), `UpdatedAt` last-write-wins (newer in / older in / equal), running-order Guid collision warning, stage-name remap success (different local id, same name), stage-name remap failure (entire RO skipped + warning), merged `AppState` byte-stability under sorted Guids, schema-version mismatch parity with Replace, `ShowData` untouched on Merge.
- `BundleServiceTests.cs` — adjust existing Replace tests to the new `ImportBundle` signature (default arg keeps them compiling, but assert `result.Merge is null` once to lock the contract).

### Plans / docs (`Docs/Plans`)

- `003-bundle-zip-export-import.md` — flip `## Status` to `Superseded by 005 (partial)`; only the "Replace-only" decision and the Out-of-scope "Merge-on-import" note are superseded. Format, manifest, size cap, schema-mismatch policy, and JS interop in 003 stay authoritative.
- `readme.md` — update the index row for 003 and add a row for 005.

## Task order

Each step leaves the app compiling, runnable, and demoable.

1. **Plan & doc updates.** Add `Docs/Plans/005-bundle-merge-import.md` (this file, status flipped to `Active` on commit). Flip 003 status to `Superseded by 005 (partial)`. Update `Docs/Plans/readme.md` index. No code changes; build stays green.
2. **Result + mode types, signature widening.** Add `BundleImportMode`, `MergeStats`, extend `BundleImportResult` with `Merge`. Widen `IBundleService.ImportBundle` with optional `mode` + `currentState` arguments; `BundleService` ignores them and behaves exactly as today. Update `BundleServiceTests` Replace cases to assert `result.Merge is null`. Demoable: existing Replace flow unchanged, contract surface ready.
3. **Merge implementation in `BundleService`.** Implement `MergeInto`: upsert bands (last-write-wins by `UpdatedAt`), upsert running orders (incoming wins, warn), stage-name remap, deterministic sort. Add merge-only unit tests. Settings UI still hardcodes Replace; merge is reachable from tests only. Demoable via test suite.
4. **Settings UI: mode picker + merge dispatch.** Add Replace / Merge radio, dynamic confirm wording, merge-aware success toast formatting `MergeStats`. Wire `BandService.Snapshot()` into the merge call. Manual e2e: export from one browser profile, import-merge into another, verify upsert + preserved local entries. Demoable feature complete.
5. **Verification + plan close-out.** Run `dotnet test`, smoke-test both modes against a real bundle, then mark 005 `Active → Archived` (or leave `Active` until plan 006 needs it) and refresh the readme index.

## Implementation cadence

Three review checkpoints.

- **Wave 5a — Docs & contract (tasks 1–2).** Plan landed, types in place, no behavior change. Reviewable as a pure refactor.
- **Wave 5b — Merge engine (task 3).** Service logic + tests. Reviewable as a self-contained algorithm change with full unit coverage.
- **Wave 5c — UI & sign-off (tasks 4–5).** End-user surface and verification.

## Out of scope

- **Three-way merge / conflict UI.** Conflicts resolve via `UpdatedAt` policy; no per-field picker.
- **Stage import on Merge.** Local `ShowData.Stages` is authoritative; the sender cannot add stages via Merge. Future plan if multi-engineer stage authoring becomes a need.
- **Partial slot acceptance.** A running order with one unmappable stage is dropped whole; per-slot salvage is out of scope.
- **Merge for `ShowData`.** Replace remains the only path to overwrite festival-level metadata.
- **Persisted user preference for default mode.** Always defaults to Replace per session.

## Risks & migrations

- **`UpdatedAt` clock skew across machines.** Last-write-wins is only as honest as the local clocks. Mitigation: surface skipped bands in warnings so the user can spot reversed-skew silent drops; revisit if it bites in practice.
- **Stage rename collisions.** If two local stages share a name (case-insensitive after trim), remap is ambiguous. Mitigation: refuse the merge with an error naming the duplicate; do not silently pick one.
- **`Guid` collision across unrelated installs.** Vanishingly unlikely with v4 GUIDs but treated as a real upsert (last-write-wins). Acceptable.
- **Schema bump during 005 development.** None planned; 005 does not touch persisted shape. If a future plan bumps `AppState.SchemaVersion`, the 003 mismatch policy still applies on Merge.
- **Settings-page state on failed Merge.** `_pendingBundle` cleared on cancel and on completion; failure paths must not leave the dialog stuck. Covered by existing Replace path; reuse the same cleanup.
