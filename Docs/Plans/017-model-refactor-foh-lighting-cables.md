# 017 — Model refactor: FOH, lighting, and cables

## Status

Draft

## Context

Successor to 015 (UI density). During the v2 UI build it became clear the current model shapes are too coarse for dense editing. Specifically:
- Cabling only records a minimum length; riders often specify a range.
- Lighting floor machines have no location field and the label "Name" is ambiguous.
- FOH `OutputProtocol` and `OutputLocation` are closed enums with no escape hatch for hybrids.
- FOH `StageToFohRoundTrip` is a boolean; real riders need a send count and a separate round-trip send count.

These changes touch `AppState` nested shapes, so both the persisted JSON and the bundle CSV wire format must migrate. A state schema bump (3 → 4) and a bundle schema bump (3 → 4) are required.

## Decisions (locked)

- **Keep `LightingMachine.Name` property** — only the UI label and CSV key become `ModelOrType`; the C# property stays `Name` to avoid a pointless JSON rename migration. `Location` is added as a new nullable string.
- **Replace `bool StageToFohRoundTrip` with `int StageToFohRoundTripCount`** — the old boolean is interpreted as 0/1 during migration.
- **Closed-set enum pattern** — `OutputProtocol` and `OutputLocation` each gain an `Other` value and a paired `*Other` string property on `FohSound`, matching the existing `CablePoint.Other` + `SourceOther`/`TargetOther` pattern.
- **No default values for new fields** — nullable decimals and empty strings keep the model honest; the UI decides what "not set" looks like.

## Architecture rules

- `AppState.SchemaVersion` MUST bump from 3 to 4.
- Bundle manifest `SchemaVersion` MUST also bump from 3 to 4 (bundles carry `state.SchemaVersion`).
- A new `V3ToV4Migrator` MUST be registered in `Program.cs` as `IStateMigrator`.
- A new `V3ToV4BundleMigrator` MUST be registered in `Program.cs` as `IBundleMigrator`.
- Released migrator files MUST NOT be edited later; fixes ship as successor migrators.
- Every persisted-format change MUST update `ExportService` writer and reader together; round-trip byte-stability MUST hold.
- Every new/changed user-facing label MUST resolve through `ILocalizationService` and appear in `en.json` + `LocalizationKeys`.
- Old and new v2 editor pages MUST both compile and remain functional after each wave.

## File-by-file scope

### Models

- `Models/Cable.cs` — add `decimal? MaxLengthMeters`.
- `Models/LightingMachine.cs` — add `string? Location`.
- `Models/OutputProtocol.cs` — add `Other` member.
- `Models/OutputLocation.cs` — add `Other` member.
- `Models/FohSound.cs` — add `string? OutputProtocolOther`, `string? OutputLocationOther`; change `bool StageToFohRoundTrip` to `int StageToFohRoundTripCount`.
- `Models/AppState.cs` — bump `SchemaVersion` default from 3 to 4.

### Migrators

- `Migrators/V3ToV4Migrator.cs` — state migrator. Walks every band, converts `foh.stageToFohRoundTrip` bool → `stageToFohRoundTripCount` int (true → 1, false → 0), removes the old key, adds `outputProtocolOther`/`outputLocationOther` nulls. Adds `maxLengthMeters` null and `location` null where missing.
- `BundleMigrators/V3ToV4BundleMigrator.cs` — bundle migrator. Rewrites each `bands/*.csv` entry: renames `Tech.Foh,StageToFohRoundTrip` rows to `StageToFohRoundTripCount` with `True`/`False` converted to `1`/`0`.

### Services

- `Services/ExportService.cs` —
  - Export: emit `Tech.Cable,MaxLengthMeters`; emit `Tech.LightingMachine,Location`; emit `Tech.Foh,OutputProtocolOther`, `OutputLocationOther`, `StageToFohRoundTripCount` (removing the old `StageToFohRoundTrip` row).
  - Import: read the new keys; for backward safety during manual CSV paste, keep a fallback that maps an old `StageToFohRoundTrip` bool string to int if the new key is absent.
- `Services/StorageService.cs` — `CurrentSchemaVersion` becomes 4.

### Print strategies

- `PrintStrategies/BandRiderPrintStrategy.cs` —
  - Cabling table: add a max-length column.
  - Lighting floor machines: include `Location` in the inline summary.
  - FOH output line: use `*Other` override when enum is `Other`.
  - FOH sends: render both `StageToFohSendCount` and `StageToFohRoundTripCount`.

### Pages (both v1 and v2)

- `Pages/RiderEditor.razor` —
  - Cabling: add max-length input next to min-length.
  - Lighting floor machines: add location input; change heading label to model/type.
  - FOH: add "Other" selects with conditional override inputs; replace round-trip checkbox with a numeric round-trip count input.
- `Pages/RiderEditorV2.razor` — same field changes as v1, adapted to the dense table layout already built in 015.

### Localization

- `wwwroot/i18n/en.json` — add keys for `field.cable.maxLengthMeters`, `field.lighting.modelOrType`, `field.lighting.location`, `enum.OutputProtocol.Other`, `enum.OutputLocation.Other`, `field.foh.outputProtocolOther`, `field.foh.outputLocationOther`, `field.foh.roundTripCount`.
- `LocalizationKeys.cs` — add corresponding constants.
- `wwwroot/i18n/fr-fr.json` — add French equivalents.

### Tests

- `tests/FestivalRider.Tests/ExportServiceTests.cs` — update `FullBand` round-trip assertions for new fields.
- `tests/FestivalRider.Tests/` — add `V3ToV4MigratorTests` covering bool→int conversion and null seeding.
- `tests/FestivalRider.Tests/` — add `V3ToV4BundleMigratorTests` covering CSV row rename and value conversion.

## Task order

1. Bump `AppState.SchemaVersion` to 4 and `StorageService.CurrentSchemaVersion` to 4.
2. Add model fields (`Cable.MaxLengthMeters`, `LightingMachine.Location`, `FohSound.OutputProtocolOther`/`OutputLocationOther`/`StageToFohRoundTripCount`, enum `Other` values).
3. Build `V3ToV4Migrator` and register it.
4. Build `V3ToV4BundleMigrator` and register it.
5. Update `ExportService` CSV writer and reader for all new/changed keys.
6. Update `RiderEditor.razor` and `RiderEditorV2.razor` with new inputs.
7. Update `BandRiderPrintStrategy` rendering.
8. Add localization keys and `LocalizationKeys` constants.
9. Update tests to cover new fields and migration round-trips.
10. Verify build + test pass.

## Implementation cadence

- **Wave 1 — Models and migrations**: schema bump, model additions, `V3ToV4Migrator`, `V3ToV4BundleMigrator`. App stays runnable; old data migrates silently.
- **Wave 2 — CSV and services**: `ExportService` writer/reader updates, print strategy updates.
- **Wave 3 — UI and localization**: both rider editors, localization keys, print rendering.
- **Wave 4 — Tests and polish**: round-trip tests, migrator tests, responsive check.

## Out of scope

- Retiring old v1 pages (plan 015 decision).
- Changing cable enums or provider list.
- Any new NuGet packages.

## Risks & migrations

- **Risk**: the `StageToFohRoundTrip` bool → int conversion in both JSON and CSV is lossy if a user manually edited a v3 CSV to contain a non-boolean value. **Mitigation**: `ParseBool` already defaults to false; the migrator treats any non-`True` value as 0.
- **Risk**: old v3 bundles imported after this plan will silently migrate to v4 and re-export as v4, so a receiver on v3 cannot read them. **Mitigation**: this is expected bundle behavior; the v3 app would see schema mismatch and refuse or migrate if it had a future successor migrator.
- **Risk**: `LocalizationCatalogTests` parity failures if `en.json` keys and `LocalizationKeys` constants drift. **Mitigation**: update both in the same commit and run tests before pushing.
