# 003 — Bundle zip export/import

## Status

`Active`

## Context

Successor adjunct to [002-domain-model-revision.md](./002-domain-model-revision.md) (which remains `Active`). Wave 4 of 001 added per-entity CSV (band + show); Wave 5 adds running-order CSV. A single-file backup/share artifact is still missing: users currently have to export each band CSV individually plus the show CSV. This plan introduces a `.zip` bundle that aggregates all per-entity CSVs plus a manifest so the entire `AppState` round-trips through one download/upload.

## Decisions (locked)

- **Bundle format** — `.zip` (ZIP64-capable, `System.IO.Compression.ZipArchive`). No new NuGet package; `System.IO.Compression` is in-box for .NET 9 WASM.
- **Content** — composition of existing per-entity CSVs (no new CSV dialect). `ShowData` → `show.csv`, each `Band` → `bands/{Guid}.csv`, each `RunningOrder` → `running-orders/{Guid}.csv`. Deterministic: GUIDs sorted ascending, `\n` line endings preserved, UTF-8 no BOM.
- **Manifest** — `manifest.json` at bundle root. Fields: `format` (const `"festivalrider-bundle"`), `schemaVersion` (int, mirrors `AppState.SchemaVersion`), `exportedAt` (`DateTimeOffset`, ISO-8601 round-trip), `show` (string path), `bands` (string[]), `runningOrders` (string[]). Sort both arrays by GUID ascending for byte stability.
- **Filename** — `festivalrider-{sanitized-show-name}-{yyyyMMdd-HHmmss}.zip`. `Sanitize` rule matches existing `RiderEditor.ExportCsv`.
- **Import mode** — v1 ships **Replace** only: bundle contents wholly supplant in-memory `AppState` via [IBandService.ReplaceState](cci:1://file:///home/jorisdebet/RiderProjects/FestivalRider/Services/BandService.cs:107:4-113:5). **Merge** semantics (upsert by GUID, preserve unlisted entities) are explicitly deferred.
- **Schema-version mismatch** — on `schemaVersion != AppState.SchemaVersion`, refuse the import, log a warning, toast the user. No in-place migration (matches 001's backup-and-reset policy).
- **Size guard** — reject uploads > 20 MB (InputFile `maxSize`). Far above expected payloads (100 bands ≈ <1 MB per 001 risks).
- **JS interop** — existing [festivalRiderCsv.downloadText](cci:1://file:///home/jorisdebet/RiderProjects/FestivalRider/wwwroot/js/csvio.js:2:4-12:5) gains a sibling `downloadBytes(filename, mime, bytes)` that accepts a `Uint8Array`. No third JS file.
- **Round-trip guarantee** — re-exporting an imported bundle yields a byte-identical `.zip` given identical `AppState` (manifest `exportedAt` re-stamped, entries otherwise equal). `exportedAt` is informational; round-trip tests compare the CSV entries and manifest minus `exportedAt`.
- **Confirmation** — import calls `ConfirmDialog` with "Replace all current data with the bundle contents?" wording before dispatch.
- **Persistence coupling** — bundle import triggers the same `OnChange`/`StorageService` debounce chain as any other [ReplaceState](cci:1://file:///home/jorisdebet/RiderProjects/FestivalRider/Services/BandService.cs:107:4-113:5); no direct `localStorage` writes from the bundle service.

## Open questions

None.

## Architecture rules

Inherits 001/002 unchanged. Additional:

- **Service boundary** — bundle composition lives in `IBundleService`; it orchestrates [IExportService](cci:2://file:///home/jorisdebet/RiderProjects/FestivalRider/Services/IExportService.cs:4:0-15:1) but MUST NOT duplicate CSV logic.
- **No merge logic in v1** — import path calls [IBandService.ReplaceState](cci:1://file:///home/jorisdebet/RiderProjects/FestivalRider/Services/BandService.cs:107:4-113:5) exactly once per successful import. Partial imports (e.g. "only bands") are out of scope until merge lands.
- **Manifest is the source of truth** — the importer reads only files listed in the manifest. Unlisted entries are ignored and surfaced as warnings, never loaded.

## File-by-file scope

### Services ([/Services](cci:9://file:///home/jorisdebet/RiderProjects/FestivalRider/Services:0:0-0:0))

- `IBundleService.cs` — new interface. Surface: `byte[] ExportBundle(AppState state)`, `BundleImportResult ImportBundle(Stream zip)`.
- `BundleService.cs` — new class. Implements `IBundleService`. Constructor takes [IExportService](cci:2://file:///home/jorisdebet/RiderProjects/FestivalRider/Services/IExportService.cs:4:0-15:1) + `ILogger<BundleService>`. Export builds `ZipArchive` over a `MemoryStream`, writes `manifest.json` last to ensure entries exist. Import validates manifest first, then resolves entries, then composes a fresh `AppState` with `SchemaVersion = manifest.SchemaVersion`. Pure; no `IJSRuntime` or [IBandService](cci:2://file:///home/jorisdebet/RiderProjects/FestivalRider/Services/IBandService.cs:4:0-28:1) dependency.
- `BundleImportResult.cs` — new record. `record BundleImportResult(AppState? State, int BandCount, int RunningOrderCount, IReadOnlyList<string> Warnings, string? Error)`. `State == null ⇔ Error != null`.

### JS interop (`/wwwroot/js`)

- [csvio.js](cci:7://file:///home/jorisdebet/RiderProjects/FestivalRider/wwwroot/js/csvio.js:0:0-0:0) — extend with `downloadBytes(filename, mime, bytes)` calling `new Blob([bytes], { type: mime })`. Register the new function under the existing `window.festivalRiderCsv` namespace. Update [wwwroot/js/AGENTS.md](cci:7://file:///home/jorisdebet/RiderProjects/FestivalRider/wwwroot/js/AGENTS.md:0:0-0:0) locked surface.

### Pages ([/Pages](cci:9://file:///home/jorisdebet/RiderProjects/FestivalRider/Pages:0:0-0:0))

- [Settings.razor](cci:7://file:///home/jorisdebet/RiderProjects/FestivalRider/Pages/Settings.razor:0:0-0:0) — add a **Bundle** section above **Storage**. Two controls: `Export bundle` button → calls `IBundleService.ExportBundle` → JS `downloadBytes`; `Import bundle` `<InputFile accept=".zip">` → reads stream → calls `IBundleService.ImportBundle` → on success, `ConfirmDialog` ("Replace all…") → [IBandService.ReplaceState(result.State!)](cci:1://file:///home/jorisdebet/RiderProjects/FestivalRider/Services/BandService.cs:107:4-113:5) → toast with `BandCount` / `RunningOrderCount`. On `Error != null` or `Warnings` non-empty, toast level `Warning` / `Error` with the first two warnings concatenated.

### Program.cs

- Register `services.AddScoped<IBundleService, BundleService>()`.

### Tests (`/FestivalRider.Tests`) — deferred to Wave 9 alongside existing test plan

- `BundleServiceTests.cs` — round-trip byte stability (CSVs + manifest minus `exportedAt`), schema mismatch rejection, manifest-tampered rejection (missing file listed), unlisted-entry warning.

## CSV / manifest format

`manifest.json`:

```json
{
  "format": "festivalrider-bundle",
  "schemaVersion": 2,
  "exportedAt": "2026-04-28T13:45:00.0000000+00:00",
  "show": "show.csv",
  "bands": ["bands/3f1c…-….csv"],
  "runningOrders": ["running-orders/7a0d…-….csv"]
}
```

Serialized via `System.Text.Json` with `JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = CamelCase }`. Entry streams flushed explicitly before the archive disposes to guarantee stored bytes.

## Task order

Each step leaves the app compiling and runnable.

1. `IBundleService` + `BundleImportResult` + stub `BundleService` returning `NotImplementedException`; DI registration. Build green.
2. Implement `BundleService.ExportBundle` (manifest + show + bands + running orders). [csvio.js](cci:7://file:///home/jorisdebet/RiderProjects/FestivalRider/wwwroot/js/csvio.js:0:0-0:0) gains `downloadBytes`. Settings wire `Export bundle` button end-to-end.
3. Implement `BundleService.ImportBundle` (manifest read + entity rehydration + schema check + warnings). Settings wire `Import bundle` with confirm dialog.
4. `BundleServiceTests.cs` (pairs with Wave 9 test project; stubbed out until then).

## Implementation cadence

Single wave, review checkpoint at the end.

- **Wave 4a — Bundle zip**: tasks 1–3. Demoable outcome: round-trip a full `AppState` through a downloaded `.zip`.
- **Wave 9 delta**: task 4. Regenerated alongside existing export/running-order tests.

## Out of scope

- **Merge-on-import** — deferred; successor plan if demand surfaces.
- **Bundle diff / three-way merge** — out of scope.
- **Encryption / password protection** — out of scope; bundles are plain `.zip`.
- **Migrating v1 bundles** — v1 never shipped bundles; no migration surface.
- **Print bundle** — printing remains per-entity.

## Risks & migrations

- **Zip-slip / path traversal** — importer MUST reject manifest paths containing `..` or absolute prefixes, and MUST only open entries whose names appear in the manifest. Enforced in `BundleService.ImportBundle`.
- **Schema drift** — `schemaVersion` mismatch → hard refuse + toast. Acceptable pre-release.
- **Manifest / entry divergence** — missing listed entry → import fails with `Error`; unlisted entry → warning, skipped.
- **Partial writes on download** — `ZipArchive` disposed before returning `MemoryStream.ToArray()`; tested for byte stability.
- **`InputFile` size cap** — 20 MB ceiling; UI surfaces a toast if exceeded.
