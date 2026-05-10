# 008 — Schema migration framework

## Status

`Active`

## Context

[001-initial-plan.md](./001-initial-plan.md) locked a "backup-and-reset" policy for `AppState.SchemaVersion` mismatches: the raw payload is copied to `festivalrider.backup.v{found}` and a clean `AppState` is restored. [002-domain-model-revision.md](./002-domain-model-revision.md) bumped 1 → 2 and explicitly deferred in-place migration. [006-multi-show-support.md](./006-multi-show-support.md) (Draft) bumps 2 → 3. As real users start to accumulate data across versions, backup-and-reset is no longer acceptable: it loses every band, rider, and running order on each schema bump. This plan introduces a typed migration pipeline registered via DI, with v1 → v2 as the first concrete migrator and a contract designed to absorb v2 → v3 (006), v3 → v4, and beyond. Backup-and-reset stays as the fallback when no migration chain exists.

## Decisions (locked)

- **Migrator interface** — `interface IStateMigrator { int FromVersion { get; } int ToVersion { get; } JsonNode Migrate(JsonNode raw, IList<string> warnings); }`. Migrators operate on `System.Text.Json.Nodes.JsonNode` (not typed `AppState`) so each migrator stays decoupled from the current code shape; `AppState` will keep evolving and migrators must remain frozen against the version they came in for.
- **Step-wise migration** — each migrator advances exactly one version (`ToVersion == FromVersion + 1`). Multi-version upgrades chain stepwise. No skipping. Keeps each migrator small, testable, and reviewable.
- **DI registration** — every `IStateMigrator` registered as `Scoped` (per AGENTS.md DI rule). `StorageService` resolves them via `IEnumerable<IStateMigrator>` and indexes by `FromVersion`. Duplicate `FromVersion` registrations throw at startup (loud, not silent).
- **Pipeline location** — migration runs inside `IStorageService.EnsureLoadedAsync` after parsing the raw JSON and before binding to typed `AppState`. On success: store the migrated payload back to `localStorage` under `festivalrider.state` (so subsequent loads skip the migration), bump version, surface an informational toast `"Migrated data v{from} → v{to}."`, log accumulated warnings.
- **Fallback policy** — if any of the following holds, fall back to 001's backup-and-reset: (a) parsed `schemaVersion` is not an int; (b) no migrator chain reaches the current `AppState.SchemaVersion`; (c) any migrator throws; (d) the migrated payload fails final typed deserialization. Backup key remains `festivalrider.backup.v{found}`. NEVER silently drop data without a backup.
- **Warnings surface** — each migrator MAY append human-readable strings to the `warnings` list (e.g. `"Dropped 3 InputChannel rows; v2 has no equivalent."`). `StorageService` flushes the warnings to the toast service after the migration completes; first 3 displayed, full list logged.
- **v1 → v2 migrator (concrete)** — `Migrators/V1ToV2Migrator.cs`. Maps the 001 single-`Band`-list shape onto 002's structured `AppState`: drops `Band.Genre` (warn once), drops every `Band.Rider.Tech.Inputs` and `Backline*` collection (warn with counts), wraps the (then-absent) `ShowData` as a freshly-defaulted root, leaves `RunningOrders` intact (their stage references default to "Unknown stage" if no v1 stage data was retained — surfaced in the existing UI per 002's risk note). Does not invent data.
- **Future v2 → v3 migrator** — owned by [006-multi-show-support.md](./006-multi-show-support.md). 008 reserves `Migrators/V2ToV3Migrator.cs` as the destination path so 006 doesn't relitigate the framework.
- **No ad-hoc migration** — code outside `Migrators/` MUST NOT inspect or transform persisted JSON. `StorageService` is the only host for the pipeline.
- **Idempotency** — running the pipeline twice on the same payload MUST be a no-op (each migrator only fires when `parsed.schemaVersion == FromVersion`). The post-migration write covers this in practice; the property is enforced by tests.
- **Testability** — migrators are pure functions of `(JsonNode in) → JsonNode out`. No `IJSRuntime`, no logger injection. The accumulator `warnings` list is the only observable side effect. Tests exercise migrators in isolation without touching `StorageService`.

## Open questions

None.

## Architecture rules

Inherits 001 / 002 / 005 / 006 unchanged. Additional:

- A new folder `src/FestivalRider/Migrators/` MUST hold all `IStateMigrator` implementations. AGENTS.md gains a layering note for this folder.
- Migrators MUST be frozen once shipped: editing a released migrator is forbidden (changes the migration outcome for users mid-upgrade). Bug fixes land as successor migrators or as a one-off "v{n} → v{n} repair" migrator with explicit user-facing rationale in a successor plan.
- `StorageService` MUST persist the migrated payload before raising `OnChange`, so a crash mid-load doesn't trap the user in a perpetual migration loop.
- Backup-and-reset MUST remain reachable as a last resort and MUST NOT be removed by this plan.

## File-by-file scope

### Services / migrators (`src/FestivalRider/Migrators`, `src/FestivalRider/Services`)

- `Migrators/IStateMigrator.cs` — new interface as above.
- `Migrators/V1ToV2Migrator.cs` — first concrete migrator. `FromVersion = 1`, `ToVersion = 2`. Drops `Genre`, `InputChannel`, `Backline*` with counted warnings; defaults `ShowData`; preserves `RunningOrders`.
- `Migrators/AGENTS.md` — new file. Single concept: migrators are frozen on ship; one file per `(from, to)` pair; no service injection.
- `Services/StorageService.cs` — extend `EnsureLoadedAsync`: parse raw JSON → read `schemaVersion` → resolve migrator chain via injected `IEnumerable<IStateMigrator>` → run stepwise → on success persist + toast, on failure backup-and-reset.
- `Services/IStorageService.cs` — surface unchanged.
- `Program.cs` — register `services.AddScoped<IStateMigrator, V1ToV2Migrator>()`. Future migrators append to this list.

### Tests (`tests/FestivalRider.Tests`)

- `Migrators/V1ToV2MigratorTests.cs` — table-driven tests covering: minimal v1 payload (just bands), v1 with full TechRider (asserts dropped fields counted in warnings), v1 with running orders (preserved), malformed v1 (migrator throws → caller fallbacks). Pure JSON in / JSON out, no service deps.
- `StorageServiceTests.cs` — new cases: chain from v1 → v2 succeeds, persists migrated payload, raises toast; missing-chain payload (e.g. v0) hits backup-and-reset; throwing migrator hits backup-and-reset; idempotent re-load (second `EnsureLoadedAsync` is a no-op).
- `TestDataFactory.cs` — add `BuildV1JsonPayload(...)` helper if not present.

### Docs (`Docs/Plans`)

- `001-initial-plan.md` — leave status as `Superseded by 002 (partial)`. Append nothing inline; the backup-and-reset clause is amended-with-fallback by 008, not invalidated. Note in this plan's Context (already done).
- `002-domain-model-revision.md` — already `Active` (or `Superseded by 006 (partial)` once 006 lands). 008 doesn't change its status.
- `006-multi-show-support.md` — relies on 008 for the v2 → v3 migrator file. Cross-link maintained.
- `readme.md` — index gets a row for 008.

### Root config

- `AGENTS.md` (root) — add a single rule under Layering / Persistence: "ALL persisted-shape transformations MUST live under `src/FestivalRider/Migrators/`. NEVER inline schema migration in `StorageService` or any other service." Released migrator files MUST NOT be edited.

## Task order

Each step leaves the app compiling and runnable. Steps grouped impl → revisit → tests → docs.

### Implementation

1. **Framework + DI, no migrators.** Add `IStateMigrator`, extend `StorageService` to read schemaVersion, resolve `IEnumerable<IStateMigrator>` (empty), and continue to backup-and-reset on mismatch (current 001 behavior). Build green, behavior unchanged.
2. **v1 → v2 migrator.** Add `Migrators/V1ToV2Migrator.cs`, register in `Program.cs`. Wire the migration pipeline in `StorageService` (chain resolution, stepwise apply, post-migration persist + toast, fallback on throw). Manual e2e: prime localStorage with a hand-crafted v1 payload, reload, observe migration toast and intact bands.
3. **Migrators folder hygiene.** Add `Migrators/AGENTS.md` and update root `AGENTS.md` with the new layering rule.

### Revisit checkpoint

4. **Triage post-impl.** Test the pipeline with three real-world v1 payloads (minimal / typical / heavy with dropped fields). If the warning surface is too chatty (or too quiet), or if any v1 payload trips an unexpected fallback, amend Decisions here before tests. If schema-bump 002 → 003 (006) is in flight in parallel, lock-in the v2 → v3 migrator destination filename now.

### Tests

5. **Migrator unit tests.** `Migrators/V1ToV2MigratorTests.cs` — pure JSON cases.
6. **StorageService integration tests.** Chain success, missing-chain fallback, throwing-migrator fallback, idempotent re-load. Use `FakeJSRuntime` for `localStorage` reads/writes; do NOT touch real `localStorage`.

### Docs

7. **Status & index sync.** Add 008 row to `Docs/Plans/readme.md`. Cross-reference 008 from 006 if 006 is `Active` at this point. Confirm root `AGENTS.md` reflects the new layering rule.

## Implementation cadence

- **Wave 8a — Framework (step 1).** Demoable: build green, no behavior change, scaffolding ready.
- **Wave 8b — v1 → v2 migrator (steps 2–3).** Demoable: real v1 payloads upgrade in place with no data loss beyond the explicitly-dropped fields.
- **Wave 8c — Triage (step 4).** Reviewable as no-op or amendment.
- **Wave 8d — Tests (steps 5–6).** Reviewable as a green suite expansion.
- **Wave 8e — Docs (step 7).** Index + AGENTS sync.

## Out of scope

- **Downgrade migrations.** Strictly forward-only.
- **Cross-version repair migrators (`vN → vN`).** Possible via a successor plan; not shipped here.
- **User-driven migration toggles.** Migration is automatic on load; no opt-out switch.
- **Migration of bundle payloads.** 003/005 still hard-refuse on bundle schema mismatch. Bundle migrators are a deliberate future concern.
- **Telemetry for migration outcomes.** No analytics surface; warnings are local toast + log.

## Risks & migrations

- **Frozen-migrator discipline.** Once a user has migrated v1 → v2, editing `V1ToV2Migrator.cs` retroactively changes nothing for them but corrupts new v1 imports. Mitigation: AGENTS rule + reviewer enforcement + the explicit "successor migrator" escape hatch.
- **Crash mid-migration.** Migrated payload is persisted before `OnChange` fires. If persistence fails (`localStorage` quota), the pipeline reverts to backup-and-reset and surfaces the failure as an error toast.
- **Partial schema awareness drift.** `JsonNode`-typed migrators decouple from current model code, but if a future version renames the root `schemaVersion` field, the chain breaks silently. Mitigation: `StorageService` asserts the field name `schemaVersion` exists post-migration; absence triggers backup-and-reset.
- **Warning fatigue.** Three-warning toast cap with full log dump. Acceptable.
- **DI duplicate `FromVersion`.** Throws at startup. Better than ambiguous chain resolution at load.
- **Coordination with 006.** If 006 ships before 008's framework lands, 002 → 003 falls back to backup-and-reset. Acceptable but ugly; cross-referenced in 006's Risks.
