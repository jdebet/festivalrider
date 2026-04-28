# 001 — Initial plan

## Status

`Superseded by 002` (partial) — model file-by-file scope and CSV section list superseded by [002-domain-model-revision.md](./002-domain-model-revision.md); all other sections (architecture, services, persistence, print pipeline, deployment, testing) remain authoritative.

## Context

FestivalRider is a static Blazor WebAssembly 9 PWA deployed to GitHub Pages. It manages technical and hospitality riders for festival bands plus per-festival running orders. There is no backend: state lives in-memory at runtime and persists to `localStorage` between sessions; CSV is the user-facing share/backup format. The app builds on the stock .NET 9 Blazor WASM template already present in the repo (`FestivalRider.csproj`, `Program.cs`, `App.razor`, `Layout/`, `Pages/`).

## Decisions (locked)

- **Stack** — Blazor WebAssembly 9, Bootstrap 5 (already wired by the template), CsvHelper, xUnit. No additional NuGet packages without a successor plan.
- **DI lifetime** — `Scoped` for every service. Functionally identical to `Singleton` in WASM; `Scoped` is the idiomatic choice and keeps the code portable to Blazor Server later.
- **Mutability** — `class` with `{ get; set; }` for entities mutated through the UI (`Band`, `Rider`, `TechRider`, `HospitalityRider`, `RunningOrder`). `record` for immutable value leaves (`RunningOrderSlot`). `enum` for closed sets (`ContactRole`, `BacklineCategory`).
- **State management** — `BandService` holds the in-memory list and exposes `event Action? OnChange`. Pages subscribe in `OnInitialized` and unsubscribe in `Dispose` (implement `IDisposable`).
- **Persistence** — `StorageService` serializes the full `AppState` to `localStorage` under key `festivalrider.state` as JSON via `System.Text.Json`. Writes are debounced 1s via `CancellationTokenSource` + `Task.Delay`. A `beforeunload` JS hook flushes the pending write synchronously.
- **Schema versioning** — `AppState.SchemaVersion: int`, starts at `1`. On load, mismatch copies the raw payload to `festivalrider.backup.v{found}` and starts fresh, surfaced via toast. Migrations are added per successor plan.
- **Multi-tab** — single-tab assumption. `StorageService` writes a `festivalrider.tab-lock` heartbeat every 2s; second tab detects an active lock and renders a banner that disables editing. Live cross-tab sync is explicitly out of scope.
- **Resume UX** — silent auto-load on startup. Toast: "Restored N bands from previous session". `Settings` page exposes "Clear all data" and "Force save".
- **Export format** — long-format CSV per band: header `Section,Key,Value,Index,Notes`. Round-trip lossless. Sections: `Band`, `Contact`, `Tech`, `Input`, `Backline`, `Hospitality`. Running orders export as conventional tabular CSV (one row per slot), with per-stage and per-band slices.
- **Print pipeline** — print-stylesheet approach via `window.print()`. No .NET PDF library. Strategy pattern (`IPrintStrategy`) selects content shape: band rider, per-stage, per-role.
- **PDF abstraction** — `IPdfExportService.PrintAsync(strategy, context)` works today; `RenderToPdfAsync(strategy, context)` is declared but returns `null` and logs a warning, reserved for a jsPDF (or equivalent) swap without changing call sites.
- **Print page UX** — manual "Print" button. No auto-fire of `window.print()` on load (Safari is unreliable; user agency is safer).
- **Layout** — `EmptyLayout` for `/print/*` routes, `MainLayout` everywhere else. Print routes are never added to `NavMenu`.
- **Service worker** — kept enabled (offline PWA). Cache-bust handled by the published `service-worker-assets.js` hash. A `UpdateAvailableToast` component listens for `controllerchange` via `sw-update.js` and prompts reload.
- **GitHub Pages deployment** — `<base href="/FestivalRider/">` rewritten in CI, `wwwroot/.nojekyll`, `wwwroot/404.html` SPA fallback, deploy from `main` to `gh-pages` via GitHub Actions (`peaceiris/actions-gh-pages` or equivalent). Local dev keeps `<base href="/">`.
- **Validation** — `System.ComponentModel.DataAnnotations`, surfaced via `EditForm` + `DataAnnotationsValidator` + `ValidationSummary`.
- **Logging** — `ILogger<T>` injected into every service. Default WASM console sink.
- **Testing** — separate `FestivalRider.Tests` xUnit project in the solution. Services are tested through their interfaces; JS interop is faked.

## Open questions

None. All architectural choices are locked above. Future ambiguity goes into a successor plan.

## Architecture rules

- **Models** are data only: properties + DataAnnotations + value-comparison semantics. No methods, no logic, no service references.
- **Services** hold all logic. Every service has a public interface (`I{Name}Service`); pages and components depend on the interface, never the concrete type. Service signatures are async by default.
- **Pages** orchestrate: inject services, subscribe to `BandService.OnChange`, pass data and `EventCallback`s down to components, dispose subscriptions.
- **Components** are dumb: receive `[Parameter]`, emit `EventCallback`, never inject services. They may own transient UI state (form buffers, expand/collapse, hover) but never domain state.
- **One concept per file.** Folders stay flat.
- **No new NuGet packages** without a successor plan amending the locked stack list.
- **No `record` for entities mutated via the UI.** Two-way binding requires `{ get; set; }`; mutating records via `with` is non-beginner-readable and cascades through nested collections.

## File-by-file scope

### Models (`/Models`)

- `Band.cs` — `class Band` with `Guid Id`, `string Name`, `string? Genre`, `string? Notes`, `Rider Rider`, `List<Contact> Contacts`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`. `[Required]` on `Name`. `Id` defaults to `Guid.NewGuid()`.
- `Contact.cs` — `class Contact` with `ContactRole Role`, `string Name`, `string? Email`, `string? Phone`. `[EmailAddress]` on `Email`.
- `ContactRole.cs` — `enum { TourManager, BandManager, FOHEngineer, MonitorEngineer, StageManager, BackingTech, Other }`.
- `Rider.cs` — `class Rider` with `TechRider Tech`, `HospitalityRider Hospitality`. Default-constructs both.
- `TechRider.cs` — `class TechRider` with `string? PASystem`, `int MonitorCount`, `List<InputChannel> Inputs`, `List<BacklineItem> Backline`, `string? PowerRequirements`, `string? StagePlotNotes`, `int CrewCount`.
- `HospitalityRider.cs` — `class HospitalityRider` with `string? DressingRoomNotes`, `string? CateringNotes`, `List<string> DrinksRequests`, `string? DietaryRestrictions`, `int TowelCount`, `int ParkingSpaces`, `string? Accommodations`.
- `InputChannel.cs` — `class InputChannel` with `int Number`, `string Source`, `string? MicPreference`, `string? StandType`, `string? Notes`.
- `BacklineItem.cs` — `class BacklineItem` with `BacklineCategory Category`, `string Item`, `bool ProvidedByVenue`, `string? Notes`.
- `BacklineCategory.cs` — `enum { Drums, Bass, Guitar, Keys, DJ, Other }`.
- `RunningOrder.cs` — `class RunningOrder` with `Guid Id`, `string FestivalName`, `DateOnly Date`, `List<RunningOrderSlot> Slots`. `[Required]` on `FestivalName`.
- `RunningOrderSlot.cs` — `record RunningOrderSlot(Guid BandId, string Stage, TimeOnly StartTime, int SetLengthMinutes, int ChangeoverMinutes, string? Notes)`.
- `AppState.cs` — `class AppState` with `int SchemaVersion = 1`, `List<Band> Bands`, `List<RunningOrder> RunningOrders`. The single object persisted to `localStorage`.

### Service interfaces (`/Services`)

- `IBandService.cs` — read-only collections `Bands`, `RunningOrders`; `event Action? OnChange`; CRUD `Add/Update/Delete/Find` for both bands and running orders; `ReplaceState(AppState)` and `Snapshot(): AppState` for import/export round-trips.
- `IStorageService.cs` — `Task EnsureLoadedAsync()` (idempotent), `Task FlushAsync()`, `Task ClearAsync()`, `bool AnotherTabActive`, `event Action? OnAnotherTabChanged`.
- `IExportService.cs` — `string ExportBandCsv(Band)`, `Band ImportBandCsv(string csv)`, `string ExportRunningOrderCsv(RunningOrder)`, `string ExportRunningOrderByStageCsv(RunningOrder, string stage)`, `string ExportRunningOrderByBandCsv(RunningOrder, Guid bandId)`.
- `IPdfExportService.cs` — `Task PrintAsync(IPrintStrategy strategy, object context)`, `Task<byte[]?> RenderToPdfAsync(IPrintStrategy strategy, object context)` (returns `null` and logs a warning today; jsPDF swap target).

### Service implementations (`/Services`)

- `BandService.cs` — implements `IBandService`. Maintains a single `AppState` instance. Every mutation updates `Band.UpdatedAt = DateTimeOffset.UtcNow` and raises `OnChange`. Throws on duplicate IDs.
- `StorageService.cs` — implements `IStorageService`. Subscribes to `IBandService.OnChange`; debounces 1s via `CancellationTokenSource` + `Task.Delay`. Reads/writes `localStorage` via `IJSRuntime` calls into `wwwroot/js/storage.js`. On load: deserializes, checks `SchemaVersion`; on mismatch backs up the raw payload to `festivalrider.backup.v{found}` and resets to a clean state, with a toast. Tab heartbeat in `festivalrider.tab-lock` every 2s; another tab detected → `AnotherTabActive=true` + raise `OnAnotherTabChanged`. Registers a `beforeunload` flush via JS so pending writes don't lose the last second of edits.
- `ExportService.cs` — implements `IExportService` using CsvHelper. Long-format CSV (`Section,Key,Value,Index,Notes`) for bands; tabular CSV for running orders.
- `PdfExportService.cs` — implements `IPdfExportService`. `PrintAsync` builds a `/print/{strategyKey}/{contextId}` URL and either navigates or opens a new window (decided per call), then invokes `triggerPrint()` once the page reports ready. `RenderToPdfAsync` logs and returns `null`; comment marker `// SWAP: jsPDF implementation goes here` so the future replacement is unambiguous.

### Print strategies (`/PrintStrategies`)

- `IPrintStrategy.cs` — `string Key { get; }`, `string GetTitle(object context)`, `RenderFragment Render(object context)`. Strategies registered in DI as `IPrintStrategy` and resolved by `Key` in `RiderPrint.razor`.
- `BandRiderPrintStrategy.cs` — `Key = "band"`. Context: `Guid bandId`. Renders band header, contacts, tech rider, hospitality rider.
- `StagePrintStrategy.cs` — `Key = "stage"`. Context: `record StageContext(Guid RunningOrderId, string Stage)`. Renders all slots and the relevant rider sections for one stage.
- `RolePrintStrategy.cs` — `Key = "role"`. Context: `record RoleContext(Guid RunningOrderId, ContactRole Role)`. Renders only the slice each role needs (e.g. FOH = inputs + monitor count + FOH contact per band).

### Pages (`/Pages`)

- `BandList.razor` — `@page "/"`. Replaces template `Home.razor`. Lists bands via `BandCard`, add new, delete with confirm, navigate to editor and print.
- `RiderEditor.razor` — `@page "/band/{Id:guid}"`. `EditForm` over a `Band` instance with `DataAnnotationsValidator` + `ValidationSummary`. Uses `RiderSection` for collapsible Tech / Hospitality blocks. Add/remove rows for `Inputs`, `Backline`, `Contacts`, `DrinksRequests`. Save calls `IBandService.Update`.
- `RunningOrder.razor` — `@page "/running-order"`. List, create, edit running orders and slots; per-stage and per-band CSV exports.
- `RiderPrint.razor` — `@page "/print/{StrategyKey}/{ContextId}"`, `@layout EmptyLayout`. `OnInitializedAsync` awaits `IStorageService.EnsureLoadedAsync()`, resolves the matching `IPrintStrategy` from `IEnumerable<IPrintStrategy>`, deserializes `ContextId` per strategy, renders. Visible "Print" button calls `IPdfExportService.PrintAsync`. Missing entity → simple 404 panel.
- `Settings.razor` — `@page "/settings"`. Clear all data (with confirm), force flush, schema version display, backup download, CSV import/export entry points.

### Components (`/Components`)

- `BandCard.razor` — `[Parameter] Band Band`, `[Parameter] EventCallback<Guid> OnEdit / OnDelete / OnPrint`.
- `RiderSection.razor` — collapsible section. `[Parameter] string Title`, `[Parameter] RenderFragment ChildContent`, internal `bool _expanded`.
- `UpdateAvailableToast.razor` — listens (via JS interop on `sw-update.js`) for new service-worker activation; prompts reload.
- `MultiTabBanner.razor` — visible when `IStorageService.AnotherTabActive` is true; subscribes to `OnAnotherTabChanged`.
- `ConfirmDialog.razor` — generic confirm modal used by destructive actions.

### Layout (`/Layout`)

- `EmptyLayout.razor` — `@inherits LayoutComponentBase` with just `@Body`. Used by `/print/*`.
- `MainLayout.razor` (existing) — add `<UpdateAvailableToast />` and `<MultiTabBanner />`.
- `NavMenu.razor` (existing) — add Bands / Running order / Settings links. Print routes are never linked.

### JS interop (`/wwwroot/js`)

- `storage.js` — `getItem(key)`, `setItem(key, value)`, `removeItem(key)`, `registerBeforeUnload(dotNetRef, methodName)`, `registerStorageEvent(dotNetRef, methodName)` (for tab-lock detection).
- `print.js` — `triggerPrint()` calls `window.print()`.
- `sw-update.js` — listens for service-worker `controllerchange`; invokes a .NET callback to show the update toast.

### Static assets (`/wwwroot`)

- `index.html` (existing) — `<base href="/">` for dev (CI rewrites to `/FestivalRider/`); register the three JS files; service-worker registration kept (template default).
- `.nojekyll` — empty file, prevents GitHub Pages from stripping `_framework/*`.
- `404.html` — SPA fallback that rewrites the URL into a query string and reloads `index.html` (rafgraph/spa-github-pages technique).
- `css/print.css` — `@media print` rules: hide nav and buttons, page-break rules per rider section, monochrome-friendly tables.

### CI (`/.github/workflows`)

- `deploy.yml` — checkout, `dotnet publish -c Release -p:BlazorWebAssemblyBaseHref=/FestivalRider/`, copy `.nojekyll` and `404.html` into the publish output if not already there, push the published `wwwroot` to `gh-pages` via `peaceiris/actions-gh-pages`. Service-worker cache-bust handled by the published `service-worker-assets.js` hash.

### Tests (`/FestivalRider.Tests`)

- `FestivalRider.Tests.csproj` — xUnit, references the main project.
- `BandServiceTests.cs` — CRUD, `OnChange` raised, duplicate ID rejection.
- `ExportServiceTests.cs` — CSV round-trip is byte-stable; running-order slicing.
- `StorageServiceTests.cs` — schema-mismatch backup path; debounce coalescing (with a fake `IJSRuntime` and a virtual clock).
- `PrintStrategyTests.cs` — content selection per strategy.

### `Program.cs` (existing)

- Add scoped registrations for `IBandService`, `IStorageService`, `IExportService`, `IPdfExportService` and their concrete impls; register every `IPrintStrategy` via `services.AddScoped<IPrintStrategy, BandRiderPrintStrategy>()` etc. (DI resolves `IEnumerable<IPrintStrategy>` automatically). Keep `App` root and the existing `HttpClient` registration.

## CSV format

Long-format CSV per band uses header `Section,Key,Value,Index,Notes`. `Index` disambiguates list rows (`Inputs[i]`, `Contacts[i]`, etc.); empty for scalar fields. Section order is fixed: `Band, Contact, Tech, Input, Backline, Hospitality`. Within a section keys are emitted in declaration order so diffs stay readable.

Example for one band:

```csv
Section,Key,Value,Index,Notes
Band,Id,3f1c…,,
Band,Name,Iron Maiden,,
Band,Genre,Heavy Metal,,
Contact,TourManager,Jane Doe,0,jane@example.com
Contact,FOHEngineer,Bob Smith,0,
Tech,PASystem,L-Acoustics K2,,
Tech,MonitorCount,12,,
Input,Source,Kick In,1,Beta 91A
Input,Source,Kick Out,2,Beta 52
Backline,DrumKit,DW Collector's,0,Provided by venue
Hospitality,DressingRoomNotes,2 rooms with mirrors,,
```

Round-trip is deterministic: the same `Band` always produces the same CSV byte-for-byte. Running orders use a conventional tabular CSV — one row per slot with columns `Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes`.

## Task order

Each step leaves the app compiling and runnable.

1. **Models** — `Band`, `Contact`, `ContactRole`, `Rider`, `TechRider`, `HospitalityRider`, `InputChannel`, `BacklineItem`, `BacklineCategory`, `RunningOrder`, `RunningOrderSlot`, `AppState`.
2. **DI + interfaces + stub services** — service interfaces, empty implementations that satisfy them, registered in `Program.cs`. `EmptyLayout`. App still runs.
3. **`BandService`** — full CRUD, `OnChange`, in-memory only.
4. **`BandList` + `BandCard`, `RiderEditor` + `RiderSection`, `ConfirmDialog`** — first usable app, no persistence.
5. **`StorageService`** — debounce, `beforeunload` flush, schema-version backup-and-reset, tab-lock heartbeat, `MultiTabBanner`. Reload survives.
6. **`ExportService`** — long-format CSV round-trip + UI hooks in `Settings` and `RiderEditor`.
7. **`RunningOrder` page** — slots CRUD, per-stage and per-band CSV exports.
8. **Print pipeline** — `IPrintStrategy`, `BandRiderPrintStrategy`, `RiderPrint.razor`, `print.js`, `IPdfExportService`, "Print" button on `BandCard` and `RiderEditor`. End-to-end print works for one format.
9. **Stage and Role strategies** — `StagePrintStrategy`, `RolePrintStrategy`, registered in DI, surfaced in `RunningOrder.razor`.
10. **GH Pages deploy** — `.nojekyll`, `404.html`, base-href rewrite, `deploy.yml`, `sw-update.js` + `UpdateAvailableToast`.
11. **Tests** — `FestivalRider.Tests` xUnit project; tests for `BandService`, `ExportService`, `StorageService`, print strategies.

## Implementation cadence

Each wave produces a runnable, demoable app. Review checkpoint after every wave.

- **Wave 1 — Foundations.** Tasks 1–2. Models, DI, service interfaces and stub impls, `EmptyLayout`. App compiles, no features.
- **Wave 2 — CRUD.** Tasks 3–4. `BandService` + `OnChange`, `BandList`, `RiderEditor`, components. First usable app, in-memory only.
- **Wave 3 — Persistence.** Task 5. `StorageService` (debounce, `beforeunload`, schema versioning, tab-lock). Reload survives.
- **Wave 4 — Import/Export.** Task 6. `ExportService` long-format CSV round-trip + UI hooks.
- **Wave 5 — Running order.** Task 7.
- **Wave 6 — Print pipeline.** Task 8. End-to-end print for the band-rider format.
- **Wave 7 — Remaining strategies.** Task 9.
- **Wave 8 — GH Pages deploy.** Task 10. Live site, cache-bust working, update prompt verified.
- **Wave 9 — Tests.** Task 11.

Sessions can be collapsed if reviewer prefers fewer checkpoints: **(1+2)**, **(3+4)**, **(5+6)**, **(7+8+9)**.

## Out of scope

- jsPDF rendering — the interface (`IPdfExportService.RenderToPdfAsync`) is reserved; implementation deferred to a successor plan.
- Live cross-tab synchronization — single-tab assumption with detection. Successor plan if needed.
- Authentication, multi-user, server backend, hosted database.
- Mobile-app shell (Capacitor / MAUI Blazor Hybrid).
- Internationalization.

## Risks & migrations

- **Schema bump** — when `AppState` shape changes, increment `SchemaVersion`. `StorageService.LoadAsync` either runs a migration or backs up the raw payload to `festivalrider.backup.v{found}` and starts fresh, with a toast. Migrations are added in successor plans.
- **`localStorage` quota (~5 MB)** — on `QuotaExceededError`, surface a toast with the recommended action (export to CSV, then "Clear all data"). Even a busy festival of 100 bands with rich riders is well under 1 MB of JSON, so this is a guard, not an expected condition.
- **Service-worker stale cache** — published `service-worker-assets.js` hash bumps on every deploy; new SW activates → `controllerchange` fires → `UpdateAvailableToast` prompts reload. Verify manually after the first deploy.
- **Direct `/print/{...}` URL on cold load** — `RiderPrint.razor` awaits `IStorageService.EnsureLoadedAsync()` before resolving the entity; missing → 404 panel. Required for GH Pages deep-link via `404.html` SPA fallback.
- **CSV round-trip drift** — round-trip tested in `ExportServiceTests`; section order and key order are fixed in code, not data-driven, so additions require a deliberate code change.
