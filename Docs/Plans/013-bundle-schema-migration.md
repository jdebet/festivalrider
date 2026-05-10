# 013 — Bundle schema migration

## Status

`Active`

## Context

[003-bundle-zip-export-import.md](./003-bundle-zip-export-import.md) locked a "hard refuse on schema mismatch" policy for `.zip` bundle imports, and [005-bundle-merge-import.md](./005-bundle-merge-import.md) inherited it for Merge mode. [008-schema-migration.md](./008-schema-migration.md) introduced an `IStateMigrator` pipeline for the persisted `localStorage` state but explicitly listed bundle migration as out of scope. With 008 now `Active` and 006 having bumped `AppState.SchemaVersion` to 3, every v2 bundle a user has on disk is rejected at import (`"Bundle schemaVersion 2 does not match expected 3."`). Backup-and-reset is not even reachable for bundles — there's no recovery path. This plan extends the migration framework to bundles via a parallel `IBundleMigrator` chain. The bundle wire format (zip layout, manifest schema, 20 MB cap, JS interop) is otherwise unchanged.

## Decisions (locked)

- **Separate migrator surface** — `interface IBundleMigrator { int FromVersion { get; } int ToVersion { get; } void Migrate(BundleScratch scratch, IList<string> warnings); }`. Distinct from `IStateMigrator` because bundle payloads are a manifest + multiple CSV files; a single `JsonNode` is the wrong shape. Each migrator mutates a mutable scratch model (manifest dict + entry name → text dict) in place.
- **Step-wise migration** — `ToVersion == FromVersion + 1`. No skipping. Mirrors 008.
- **DI registration** — every `IBundleMigrator` registered `Scoped` in `Program.cs`. `BundleService` resolves them via `IEnumerable<IBundleMigrator>` and indexes by `FromVersion`. Duplicate `FromVersion` registrations throw at startup.
- **Pipeline location** — runs inside `BundleService.ImportBundle` immediately after `manifest.json` is parsed and the `format` field is validated, and before any per-entity decode. On success: continue with the migrated scratch as if it had arrived at `CurrentSchemaVersion`. On failure: keep 003's hard-refuse contract (return `BundleImportResult` with `Error`).
- **Failure message** — when the chain cannot reach `CurrentSchemaVersion`, the import error names the missing step: `"Bundle schemaVersion {found} cannot upgrade to v{current}: no migrator covers v{n}→v{n+1}."`. Existing "schema too old; regenerate from v{current}" message is reserved for the case where no migrators are registered at all (parity with 003's pre-008 behavior).
- **Warnings surface** — bundle-migration warnings are appended to the existing `BundleImportResult.Warnings` list. Toast cap behavior already lives in `Settings.razor`; nothing changes there.
- **Frozen on ship** — released `IBundleMigrator` files MUST NOT be edited. Bug fixes ship as a successor migrator (or a `vN→vN` repair migrator authorized by a successor plan). Mirrors 008.
- **No bundle persistence side effect** — migration runs in memory; the user's source `.zip` is never overwritten on disk. Re-exporting after import naturally produces a fresh v{current} bundle.
- **`v1` bundles** — `v1` never shipped bundle export per 003. No `V1ToV2BundleMigrator` exists or will exist.
- **First concrete migrator** — `V2ToV3BundleMigrator.cs`. Maps the v2 single-show layout onto v3 multi-show: rewrites `manifest.show` → `manifest.shows = ["shows/{newId}.csv"]` + `manifest.activeShowId = newId`; renames the bundle entry `show.csv` → `shows/{newId}.csv`; rewrites every `running-orders/{Guid}.csv` to prepend the `ShowId` column with `newId`. Mints `newId` as a fresh `Guid` (v2 manifests had no show id).
- **Reserved future migrators** — `BundleMigrators/V3ToV4BundleMigrator.cs` and beyond, owned by whichever plan bumps `AppState.SchemaVersion`. 013 reserves the path; future plans don't relitigate the framework.
- **Replace vs Merge parity** — the migration runs identically for both modes; mode is read after migration. Stage-name remap (005) operates on the migrated v{current} payload.
- **Backup behavior** — bundles are not backed up to `localStorage`. The source file is what the user holds; the failure path is "regenerate from v{current}" not "look in `festivalrider.backup.*`".
- **Idempotency** — migrating an already-current bundle is a pre-flight no-op (`foundVersion == CurrentSchemaVersion` short-circuits before the chain runs). Tests lock this property.

## Open questions

None.

## Architecture rules

Inherits 001 / 002 / 003 / 005 / 006 / 008 unchanged. Additional:

- A new folder `src/FestivalRider/BundleMigrators/` MUST hold all `IBundleMigrator` implementations and the `BundleScratch` type. AGENTS.md gains a layering note for this folder.
- Bundle migrators MUST be pure: `(BundleScratch scratch, IList<string> warnings)` only. NEVER inject services, `IJSRuntime`, `ILogger`, time, or `IStateMigrator`.
- Bundle migrators MUST NOT reference `FestivalRider.Models` types. Operate on the manifest dictionary and raw CSV strings only, so the migrator stays decoupled from current model shape.
- Bundle migrators MUST NOT call into `IStateMigrator`. The two pipelines stay independent; do not chain across surfaces.
- `BundleService` is the SOLE host of the bundle migration pipeline. NEVER inline schema transforms in `Settings.razor`, `ExportService`, or any other type.
- Released bundle migrator files MUST NOT be edited. Mirror 008's freeze rule.
- The bundle migration pipeline MUST run before any entity decode. CSV readers stay v{current}-only and never branch on schema version.

## File-by-file scope

### Bundle migrators (`src/FestivalRider/BundleMigrators`)

- `IBundleMigrator.cs` — new interface: `FromVersion`, `ToVersion`, `Migrate(BundleScratch, IList<string>)`.
- `BundleScratch.cs` — new mutable model. Public surface: `IDictionary<string, object?> Manifest` (parsed from `manifest.json`, kept as a property bag so migrators can rewrite arbitrary fields), `IDictionary<string, string> Entries` (entry full-name → UTF-8 text), `int SchemaVersion` (mirrors `Manifest["schemaVersion"]`, kept in sync by the pipeline). NEVER expose `Models` types.
- `V2ToV3BundleMigrator.cs` — first concrete migrator. Mints a fresh show `Guid`; renames `show.csv` → `shows/{Guid}.csv`; rewrites `manifest.show`/`manifest.bands`/`manifest.runningOrders` into `manifest.shows`/`manifest.activeShowId` (Bands and RunningOrders arrays unchanged); prepends `ShowId,` column to every `running-orders/{Guid}.csv` entry.
- `BundleMigrators/AGENTS.md` — new file: pure-fn rule, freeze-on-ship, one file per `(from, to)`, no model imports, no cross-pipeline calls.

### Services (`src/FestivalRider/Services`)

- `BundleService.cs` — extend constructor with `IEnumerable<IBundleMigrator>? migrators = null`. Build a `Dictionary<int, IBundleMigrator>` indexed by `FromVersion` with the same duplicate / non-stepwise checks as `StorageService` (extract a shared check helper if a third pipeline ever appears, otherwise duplicate the seven lines). Inside `ImportBundle`, after the `format` validation and before the existing schema-version comparison: build a `BundleScratch` from the archive, run the chain stepwise, then resume the existing flow against the migrated scratch (manifest decode, path validation, entity decode).
- `IBundleService.cs` — surface unchanged.

### DI (`src/FestivalRider/Program.cs`)

- Register `services.AddScoped<IBundleMigrator, V2ToV3BundleMigrator>()`. Future migrators append.

### Tests (`tests/FestivalRider.Tests`)

- `BundleMigrators/V2ToV3BundleMigratorTests.cs` — pure scratch-in / scratch-out cases: minimal v2 bundle (manifest only), v2 with one show + bands + ROs, idempotent re-run, missing `show.csv` produces a warning, non-zip-shaped scratch (manifest field type mismatch) throws.
- `BundleMigrators/BundleScratchTests.cs` — round-trip a manifest dict through `JsonSerializer` (proves the scratch shape survives the pipeline).
- `BundleServiceTests.cs` — new cases: end-to-end Replace import of a hand-rolled v2 bundle succeeds, persists nothing (in-memory only), reports warnings; Merge import of a v2 bundle migrates first then merges; missing-chain (e.g. v0 bundle) returns `Error` with the new gap message; throwing migrator returns `Error`; idempotent re-import of the migrated bundle is a no-op.
- `TestDataFactory.cs` — add `BuildV2BundleZip(...)` helper that produces a `byte[]` v2 bundle (manifest + `show.csv` + one band CSV + one RO CSV without `ShowId` column). Used by integration tests above.

### Plans / docs (`Docs/Plans`)

- `003-bundle-zip-export-import.md` — leave status `Superseded by 005 (partial)`; this plan amends only the "Schema-version mismatch — refuse" decision and the "Migrating v1 bundles" out-of-scope note. All other 003 decisions remain authoritative.
- `005-bundle-merge-import.md` — leave status `Active`; "Schema-version mismatch — same hard refusal as Replace (003)" decision is now read as "same migrate-then-fallback as Replace." No status flip.
- `008-schema-migration.md` — leave status `Active`; the "Migration of bundle payloads … deliberate future concern" out-of-scope item is the entry point for this successor plan.
- `readme.md` — add a row for 013 and link from the 003 / 008 entries' summaries if helpful.

### Root config

- `AGENTS.md` (root) — add a single rule under Persistence (or a new "Bundles" subsection): "ALL bundle-shape transformations MUST live under `src/FestivalRider/BundleMigrators/` as `IBundleMigrator` implementations. NEVER inline bundle migration in `BundleService` or any other service. Released bundle-migrator files MUST NOT be edited."

## Bundle scratch shape

`BundleScratch` is the migration-time mutable view of the bundle payload:

| Field           | Type                              | Notes                                                                                        |
| --------------- | --------------------------------- | -------------------------------------------------------------------------------------------- |
| `Manifest`      | `IDictionary<string, object?>`    | Parsed from `manifest.json` via `JsonSerializer.Deserialize<Dictionary<string, object?>>`.   |
| `Entries`       | `IDictionary<string, string>`     | Entry full-name → UTF-8 text (no BOM). Includes every non-directory zip entry.               |
| `SchemaVersion` | `int`                             | Mirrors `Manifest["schemaVersion"]`. Pipeline sets it after each migrator step.              |

After the chain completes, `BundleService` re-serializes `Manifest` back to a JSON string and re-resolves typed entity decodes against the rewritten `Entries`. The original `ZipArchive` is not consulted again.

## Task order

Each step leaves the app compiling, runnable, and demoable.

### Implementation

1. **Framework + DI, no migrators.** Add `IBundleMigrator`, `BundleScratch`, extend `BundleService` constructor with the migrator dictionary build, wire pre-flight short-circuit when `foundVersion == CurrentSchemaVersion` (existing behavior). With zero migrators registered, behavior matches 003 exactly. Build green.
2. **`V2ToV3BundleMigrator`.** Implement and register. Wire the chain into `ImportBundle` between manifest parse and entity decode. Manual e2e: hand-craft a v2 bundle in a test, import it, observe v3 `AppState`.
3. **AGENTS hygiene.** Add `BundleMigrators/AGENTS.md`; update root `AGENTS.md` with the new layering rule.

### Revisit checkpoint

4. **Triage post-impl.** Re-test with three real-world v2 bundles (export-from-002 fixtures if available, else hand-rolled). Confirm warnings surface is sane; confirm error message on missing-chain reads cleanly. If the scratch shape is awkward (e.g. `IDictionary<string, object?>` rejects deeply-nested arrays), amend Decisions before tests. Lock-in any further migrator filename if a schema bump 3 → 4 is in flight.

### Tests

5. **Migrator unit tests.** `V2ToV3BundleMigratorTests` and `BundleScratchTests`, pure scratch in / scratch out.
6. **Bundle service integration tests.** Replace + Merge happy paths on a v2 bundle, missing-chain `Error`, throwing-migrator `Error`, idempotent re-import.

### Docs

7. **Status & index sync.** Add 013 row to `Docs/Plans/readme.md`. Confirm root `AGENTS.md` reflects the new layering rule. Cross-reference 008's "Migration of bundle payloads" out-of-scope note as superseded by 013 in this plan's Context (already done).

## Implementation cadence

- **Wave 13a — Framework (step 1).** Demoable: build green, no behavior change, scaffolding ready.
- **Wave 13b — v2 → v3 migrator (steps 2–3).** Demoable: real v2 bundles import in place, surfacing the same warnings format as state migration.
- **Wave 13c — Triage (step 4).** Reviewable as no-op or amendment.
- **Wave 13d — Tests (steps 5–6).** Reviewable as a green suite expansion.
- **Wave 13e — Docs (step 7).** Index + AGENTS sync.

## Out of scope

- **Downgrade migrations.** Strictly forward-only.
- **Cross-version bundle repair migrators (`vN → vN`).** Possible via a successor plan; not shipped here.
- **User-driven migration toggle.** Migration is automatic on import; no opt-out switch. Failure surfaces as `Error`.
- **Migrating v1 bundles.** v1 never shipped bundles. No migrator surface.
- **Migrating to a `BundleScratch` typed model (`Manifest` as a record).** Property bag stays loose so future migrators can introduce/rename fields without churn. Revisit if a future plan needs static safety.
- **Sharing migrators across `IStateMigrator` and `IBundleMigrator`.** The two surfaces are deliberately distinct; a future plan may unify them if duplication proves expensive.
- **Telemetry for bundle-migration outcomes.** Local toast + log only.

## Risks & migrations

- **Frozen-migrator discipline.** Once shipped, editing `V2ToV3BundleMigrator.cs` retroactively changes outcomes for new v2 bundles. Mitigation: AGENTS rule + reviewer enforcement + successor-migrator escape hatch.
- **CSV format drift inside a migration.** v3 prepends `ShowId` to running-order CSVs. The migrator hand-edits the CSV header + every data row. Mitigation: cover header-only files, empty-rows-but-with-header, and quoted-field cases in `V2ToV3BundleMigratorTests`.
- **Manifest property bag drift.** `IDictionary<string, object?>` parsing of `manifest.json` may surface arrays as `JsonElement` rather than `List<object?>` depending on `System.Text.Json` settings. Mitigation: pin the deserialization options in `BundleScratch`'s loader and assert the expected shapes in the migrator unit tests.
- **`v2 → v3` show-id minting.** A fresh `Guid` is minted because v2 had no show id. Two imports of the same v2 bundle produce different show ids — Merge will treat them as different shows. Acceptable: the user is expected to import a given v2 bundle exactly once. Documented in the migrator's source.
- **Coordination with future schema bumps.** A schema bump that lands without its bundle migrator falls back to "Bundle schemaVersion {n} cannot upgrade to v{n+1}: no migrator covers v{n}→v{n+1}." Same coordination cost as 008 / 006 had. Acceptable.
- **Merge-on-migrated-bundle.** After migration, Merge runs unchanged. Stage-name remap and `UpdatedAt` last-write-wins still apply. Edge case: a v2 bundle's bands have v3-shaped `UpdatedAt` already (unchanged across the bump), so timestamp comparison stays meaningful. Tests assert this.
- **Refusing oversized v2 bundles before migration.** The 20 MB size cap (003) still applies and is checked before migration runs. A migrated bundle larger than the source (extra `ShowId` column) is fine because the cap is on the input file, not the in-memory scratch.
