# FestivalRider — Agent rules

## Source of truth

- ALWAYS read the `Active` plan under `Docs/Plans/` before editing code.
- NEVER edit `## Decisions (locked)` in any plan; write a successor plan instead.
- On conflict between this file and the `Active` plan, the plan wins.

## Stack

- USE Blazor WebAssembly 9, Bootstrap 5, CsvHelper, xUnit only.
- NEVER add a NuGet package without a successor plan amending `## Decisions (locked)`.
- NEVER disable `Nullable` or `ImplicitUsings`. Target stays `net9.0`.

## Layering

- Models: data only. NEVER add methods, logic, service references, or `static` factories.
- Services: all logic. ALWAYS expose an `I{Name}Service` interface; dependents bind to the interface.
- Pages: orchestrators. ALWAYS inject services and pass data to components via `[Parameter]` / `EventCallback`.
- Components: NEVER inject services. NEVER hold domain state. Inputs are `[Parameter]`, outputs are `EventCallback<T>`.
- ALWAYS one concept per file. NEVER nest subfolders inside `Models/`, `Services/`, `Components/`, `Pages/`, `PrintStrategies/`.

## DI

- ALWAYS register services and every `IPrintStrategy` implementation as `Scoped` in `Program.cs`.
- ALWAYS resolve strategies via `IEnumerable<IPrintStrategy>` matched by `Key`. NEVER add a switch-registry.

## Mutability

- USE `class { get; set; }` for UI-mutated entities (anything bound via `EditForm` or mutated from a page, e.g. `Band`, `ShowData`, `Stage`, `Rider`, `TechRider` and every sub-model under it, `TravelParty`, `Party`, `RunningOrder`, `AppState`).
- USE `record` only for immutable value leaves (`RunningOrderSlot`).
- USE `enum` only for closed sets of finite, stable values (e.g. `ContactRole`, `PartyType`, `CableType`, `PowerPhase`, `MonitorSourceMode`).
- NEVER convert a UI-mutated entity to `record`.

## Async, logging, validation

- ALWAYS make service methods async / `Task`-returning unless sync is justified inline.
- ALWAYS inject `ILogger<T>` in every service. NEVER add a third-party logger.
- USE `EditForm` + `DataAnnotationsValidator` + `ValidationSummary`. NEVER hand-roll validation.

## Persistence

- ALWAYS persist through `IStorageService`. NEVER call `IJSRuntime` for `localStorage` from any other type.
- Storage key MUST be `festivalrider.state`. `AppState` is the sole persisted root.
- ALWAYS debounce writes 1s via `CancellationTokenSource` + `Task.Delay`; the next write cancels the pending one.
- ALWAYS flush pending writes on `beforeunload`.
- ALWAYS bump `AppState.SchemaVersion` for any persisted-shape change.
- ALL persisted-shape transformations MUST live under `src/FestivalRider/Migrators/` as `IStateMigrator` implementations. NEVER inline schema migration in `StorageService` or any other service. Released migrator files MUST NOT be edited; bug fixes ship as a successor migrator.
- On schema mismatch: run the `IStateMigrator` chain inside `StorageService.EnsureLoadedAsync`. If the chain reaches `CurrentSchemaVersion`, persist the migrated payload before binding and toast `"Migrated data v{from} → v{to}."`. Otherwise fall back to: copy raw payload to `festivalrider.backup.v{found}`, reset to clean `AppState`, toast. NEVER throw.
- ALWAYS heartbeat `festivalrider.tab-lock` every 2s. Second tab MUST set `AnotherTabActive = true`; editing UI MUST disable via `MultiTabBanner`.
- NEVER implement live cross-tab sync (out of scope).

## CSV

- Band CSV header MUST be `Section,Key,Value,Index,Notes`.
- Per-band CSV section order MUST match the `Active` plan's `## CSV format` section list. Keys MUST emit in declaration order. `ShowData`/`Stage` export as a separate "show" CSV; NEVER inline them in per-band CSVs.
- Closed-set enums with an `Other`/`Custom` value MUST round-trip the paired `*Other` override string.
- Running-order CSV columns MUST be `ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes`.
- USE CsvHelper. NEVER hand-roll CSV string concatenation.
- Round-trip MUST be byte-stable. Adding a field MUST update writer, reader, and `ExportServiceTests` together.

## Print

- Print routes MUST be `/print/{strategyKey}/{contextId}` under `EmptyLayout`.
- NEVER link print routes from `NavMenu`.
- `RiderPrint.razor` MUST `await IStorageService.EnsureLoadedAsync()` before resolving the entity.
- Print MUST be user-triggered via a visible button. NEVER auto-call `window.print()` on load.
- `IPdfExportService.RenderToPdfAsync` MUST return `null` and log a warning; KEEP the `// SWAP: jsPDF implementation goes here` marker verbatim.

## GH Pages

- Local dev uses `<base href="/">`. CI MUST rewrite via `-p:BlazorWebAssemblyBaseHref=/FestivalRider/`.
- `wwwroot/.nojekyll` and `wwwroot/404.html` MUST exist. NEVER delete.
- KEEP the service worker enabled. `controllerchange` MUST trigger `UpdateAvailableToast`.

## Testing

- ALWAYS test services through their interfaces. NEVER new up a concrete service outside construction lines.
- ALWAYS fake `IJSRuntime` and time. NEVER touch real `localStorage`, `window.print()`, or the network in tests.
- Tests MUST live in `tests/FestivalRider.Tests/`. NEVER co-locate with production code.

## Task discipline

- Every commit MUST leave the app compiling and runnable.
- ALWAYS implement waves in order per the `Active` plan's `## Implementation cadence`.
