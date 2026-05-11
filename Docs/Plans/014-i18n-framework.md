# 014 — i18n framework

## Status

`Active`

## Context

FestivalRider has shipped English-only since [001-initial-plan.md](./001-initial-plan.md). Every visible string is hard-coded in Razor markup, print strategies, and inline `Toasts.Show($"...")` calls; enums render via `Enum.ToString()`; `<html lang="en">` is fixed in `index.html`; dates are emitted with `"yyyy-MM-dd"` literals in print. Crew on the European festival circuit read riders in their native language more comfortably than EN. This plan introduces a single hand-rolled localization surface (`ILocalizationService` + flat-JSON catalogs under `wwwroot/i18n/`) that replaces every user-facing string lookup, lets the user pick a locale at runtime, and pins CSV / bundle / JSON wire formats to `CultureInfo.InvariantCulture` so localization never bleeds into persisted data. The plan ships the framework, the English source-of-truth catalog, and `fr-FR` as the first translated catalog.

## Decisions (locked)

- **No new NuGet package** — the framework is hand-rolled (`ILocalizationService` + `Dictionary<string,string>`), in keeping with the AGENTS stack rule. `Microsoft.Extensions.Localization` / `.resx` / satellite assemblies are explicitly rejected: they're heavy in Blazor WASM and require build-time tooling that doesn't pay back at this scale.
- **Catalog format** — flat JSON `Dictionary<string, string>` per locale at `wwwroot/i18n/{tag}.json`. NEVER nested. NEVER per-feature splits. One catalog per locale, one HTTP fetch per `SetLocaleAsync`. Loaded via `HttpClient.GetFromJsonAsync` (already wired in `Program.cs`) and cached in-memory after first load.
- **Locale tags** — BCP-47 (`en`, `nl-BE`, `fr-BE`, ...). Service stores the tag verbatim, normalizes to lower case for catalog file lookup. Fallback chain on miss: exact tag → language-only → `en`. `en` is the absolute fallback and MUST always exist.
- **Source-of-truth catalog** — `wwwroot/i18n/en.json`. Every other catalog MUST contain the same key set with the same `{N}` positional-placeholder count per key. A build-time test (`LocalizationCatalogTests`) enforces parity and fails the build on drift.
- **Missing-key behavior** — return the English catalog value (NEVER the raw key, NEVER throw). Raw keys leaking into UI is debug-shaped and unacceptable in production. A development-only `_logger.LogWarning` records the miss; production stays silent.
- **Argument substitution** — `string.Format(CultureInfo.InvariantCulture, value, args)` with positional `{0}`, `{1}`. NEVER `CultureInfo.CurrentCulture` (would localize injected numbers / dates inside catalog strings, which the catalog author can't predict). Pages format dates via `Culture` separately, then pass the already-formatted string as `arg`.
- **Pluralization** — out of scope. Author plural-friendly source strings (`"{0} band(s)"`) or two keys (`toast.restored.one`, `toast.restored.many`). NEVER ICU MessageFormat in this plan.
- **Enum localization** — keys MUST follow `enum.{TypeName}.{ValueName}` exactly (e.g. `enum.ContactRole.TourManager`). `LocalizationCatalogTests` enumerates every enum under `FestivalRider.Models` and asserts every value has a key in `en.json`; missing entries fail the build. New enum members ship with their key in the same commit.
- **Locale persistence** — separate `localStorage` key `festivalrider.locale` storing the BCP-47 tag. NEVER part of `AppState`. NEVER part of bundles. Rationale: locale is a per-browser preference; bundle export to a colleague MUST NOT override their locale, and round-trip must not regress just because someone's browser is in NL. Schema impact: zero. No `IStateMigrator` and no `IBundleMigrator` for locale.
- **Auto-detect on first run** — when `festivalrider.locale` is absent, `LocalizationService.EnsureLoadedAsync` reads `navigator.language` via JS interop, normalizes to the closest entry in `AvailableLocales` (exact tag → language-only → `en`), and persists. Subsequent loads use the persisted value verbatim.
- **`<html lang>` sync** — `LocalizationService.SetLocaleAsync` invokes a JS interop `festivalRiderI18n.setHtmlLang(tag)` to update `document.documentElement.lang`. `index.html` ships with `lang="en"` (initial paint before the service is ready); the service rewrites on first locale resolution.
- **Service surface** — `interface ILocalizationService { string CurrentLocale; IReadOnlyList<LocaleDescriptor> AvailableLocales; CultureInfo Culture; event Action? OnLocaleChanged; Task EnsureLoadedAsync(); Task SetLocaleAsync(string tag); string T(string key); string T(string key, params object?[] args); }`. `EnsureLoadedAsync` is idempotent (mirrors `IStorageService`). `Culture` is `CultureInfo.GetCultureInfo(CurrentLocale)` cached.
- **`LocaleDescriptor`** — `record LocaleDescriptor(string Tag, string DisplayName)`. Loaded from `wwwroot/i18n/locales.json` (array of objects). Adding a locale REQUIRES editing `locales.json` AND adding `{tag}.json`. NEVER hard-code the list in C#.
- **DI** — `services.AddScoped<ILocalizationService, LocalizationService>()` per AGENTS DI rule. Resolved by every page, every print strategy, and a small set of services that build toast text (`StorageService`, `BandService`, `BundleService`).
- **Component layering — UNCHANGED** — components in `Components/` MUST NOT inject `ILocalizationService`. They receive translated strings as `[Parameter]` from their parent page / layout. This preserves the existing "components are dumb" rule. Layouts (`Layout/`) and pages (`Pages/`) MAY inject. Print strategies MAY inject (already permitted by `PrintStrategies/AGENTS.md` — "NEVER inject `IJSRuntime`" stays the only ban).
- **Wire format invariance — promoted to a hard rule** — every `string` produced for CSV, bundle JSON / CSV, bundle filenames, persisted `AppState` JSON, or any other on-disk artifact MUST go through `CultureInfo.InvariantCulture`. The current code already pins this in `ExportService` and `Settings.razor` — this plan locks it as a permanent root-AGENTS rule and adds a regression test that runs the CSV round-trip under `Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE")`. NEVER use `CultureInfo.CurrentCulture` for any persisted byte.
- **Print-strategy formatting** — print strategies use `Localization.Culture` (NOT `InvariantCulture`) for in-page date / number rendering only. Generated print HTML is locale-shaped (e.g. `9 mei 2026` under `nl-BE`). The bundle / CSV pipeline is unchanged.
- **Loading order** — `MainLayout.OnAfterRenderAsync(firstRender)` MUST `await Localization.EnsureLoadedAsync()` BEFORE `await Storage.EnsureLoadedAsync()`, so any toast emitted by storage migration / restore is already localizable. If localization fails to load, toasts fall through to English by the missing-key contract; the app stays usable.
- **Frozen catalog files** — once a release ships a catalog, key removals or `{N}` placeholder changes are breaking. Adding new keys is fine; renaming or removing requires either a migration period (both old and new keys present) or a successor plan. NEVER edit a released `*.json` to drop a key.
- **Idempotency** — re-calling `EnsureLoadedAsync` is a no-op after first success. `SetLocaleAsync(currentTag)` is a no-op (no fetch, no event). Tests pin both.
- **First non-English locale — `fr-FR`** — French (France). Ships in the same plan as wave 14h (after the EN catalog is locked by triage). Translations are user-provided; the wave scaffolds `wwwroot/i18n/fr-fr.json` with every key from `en.json` and a `"// TODO"`-style placeholder value, then accepts the user's translated values via a normal commit. The catalog parity test gates the merge.

## Open questions

None.

## Architecture rules

Inherits 001 / 002 / 005 / 006 / 008 / 013 unchanged. Additional:

- A new folder `src/FestivalRider/wwwroot/i18n/` MUST hold `locales.json` and one `{tag}.json` per locale. NEVER nest subfolders. NEVER split a locale across multiple files.
- Every user-facing string MUST resolve through `ILocalizationService.T(key, args)`. NEVER hard-code Razor markup, toast text, print-strategy strings, `<PageTitle>` content, or `aria-*` labels with English literals at the call site.
- All persisted formats (CSV, bundle JSON / CSV, `AppState` JSON, filenames) MUST use `CultureInfo.InvariantCulture` for every numeric / date / `bool` conversion. Tests pin this under a non-Invariant `CurrentCulture`.
- `ILocalizationService` MUST NOT participate in the `IStateMigrator` or `IBundleMigrator` chains. Locale is not part of `AppState` and not part of bundles.
- Components in `Components/` MUST NOT inject `ILocalizationService`. They receive translated strings via `[Parameter]`. Layouts and pages inject and pass down.
- Catalog filenames MUST match the lower-cased BCP-47 tag exactly (`en.json`, `nl-be.json`, `fr-be.json`). NEVER mixed case. NEVER aliases.
- Released catalog files (`*.json`) MUST NOT remove keys or change `{N}` placeholder counts. Adding keys is fine.

## File-by-file scope

### Services (`src/FestivalRider/Services`)

- `ILocalizationService.cs` — interface as locked. Public surface: `CurrentLocale`, `AvailableLocales`, `Culture`, `OnLocaleChanged`, `EnsureLoadedAsync`, `SetLocaleAsync`, `T(key)`, `T(key, args)`.
- `LocaleDescriptor.cs` — `public sealed record LocaleDescriptor(string Tag, string DisplayName)`.
- `LocalizationService.cs` — implementation. Holds `AvailableLocales` from `locales.json`, the active catalog `Dictionary<string, string>`, the English fallback catalog, and `Culture`. `T` performs lookup → fallback to English catalog → fallback to literal key (only if even English misses, which `LocalizationCatalogTests` makes impossible at build time). Logs missing-key once per key per session via `ILogger<LocalizationService>`.
- `StorageService.cs` — inject `ILocalizationService`; replace inline toast strings with `T("toast.restored", count)`, `T("toast.migrated", from, to)`, `T("toast.unreadable")`, etc. NO behavior change beyond string source.
- `BandService.cs` — no toast emission today; service stays untouched. (Toasts come from page handlers.)
- `BundleService.cs` — warning text emitted into `BundleImportResult.Warnings` MUST come from `T("bundle.warning.*")` keys. The `Error` string surface (e.g. `"Bundle schemaVersion {found} cannot upgrade to v{current}: ..."`) is also localized — these messages are user-visible toasts in `Settings.razor`.

### Pages (`src/FestivalRider/Pages`)

- `BandList.razor`, `RiderEditor.razor`, `RunningOrder.razor`, `Settings.razor`, `RiderPrint.razor` — inject `ILocalizationService`; subscribe to `OnLocaleChanged` like `BandService.OnChange`; replace every visible string and `<PageTitle>` content with `T(key)` / `T(key, args)`. Implement `IDisposable` (already do) to unsubscribe. NO route change.
- `Counter.razor`, `Weather.razor` — template leftovers; either delete or localize. Lock: localize (deletion is out of scope for this plan; a separate cleanup plan can drop them).

### Components (`src/FestivalRider/Components`)

- `BandCard.razor` — add `[Parameter] string EditLabel`, `PrintLabel`, `DeleteLabel`, `ContactsSummaryFormat`, `TravellersSummaryFormat`. Parent (`BandList`) supplies the translated values. NO service injection.
- `RiderSection.razor` — `Title` is already a `[Parameter]`; parent passes `T("section.title.*")`. No surface change.
- `ConfirmDialog.razor` — `Title`, `Message`, `ConfirmLabel`, `CancelLabel` are already `[Parameter]`; parents already supply translated strings. No surface change.
- `MultiTabBanner.razor` — currently has hard-coded EN copy. Promote the message to a `[Parameter] string Message`. Parent (`MainLayout`) supplies `T("banner.multitab")`.
- `ToastContainer.razor` — toast text is supplied by the producer; no change needed (text is already a parameter via `ToastMessage.Text`).
- `UpdateAvailableToast.razor` — has hard-coded EN copy AND injects `IJSRuntime`. Per components rule it MAY NOT inject `ILocalizationService`. Resolution: hoist its visible strings to `[Parameter]` and have `MainLayout` supply translated values. The `IJSRuntime` injection stays (already an established exception for this component).
- `ShowPicker.razor` — labels become `[Parameter]`. Parent (`NavMenu`) passes `T("nav.show.*")`.
- `LocalePicker.razor` — NEW. Pure presentational dropdown. Public surface: `[Parameter] IReadOnlyList<LocaleDescriptor> Locales`, `[Parameter] string CurrentTag`, `[Parameter] EventCallback<string> OnChange`. NO service injection.

### Layout (`src/FestivalRider/Layout`)

- `MainLayout.razor` — inject `ILocalizationService`; `await Localization.EnsureLoadedAsync()` BEFORE `await Storage.EnsureLoadedAsync()` in `OnAfterRenderAsync(firstRender)`; subscribe to `OnLocaleChanged` and call `StateHasChanged` so localized children re-render. Pass translated `Message` to `MultiTabBanner` and translated labels to `UpdateAvailableToast`.
- `NavMenu.razor` — inject `ILocalizationService`; render `<LocalePicker>` next to `<ShowPicker>`; replace nav labels with `T("nav.bands")`, `T("nav.runningOrder")`, `T("nav.settings")`. Subscribe to `OnLocaleChanged` (mirrors existing `BandService.OnChange` subscription pattern).
- `EmptyLayout.razor` — no change (renders only `@Body`).

### Print strategies (`src/FestivalRider/PrintStrategies`)

- `BandRiderPrintStrategy.cs`, `StagePrintStrategy.cs`, `RolePrintStrategy.cs` — inject `ILocalizationService`. Replace every section heading, table header, label, and enum-rendered string with `T(...)`. Date / number rendering uses `Localization.Culture` (NOT `InvariantCulture`); print HTML is locale-shaped.
- `IPrintStrategy.cs` — surface unchanged.

### Static assets (`src/FestivalRider/wwwroot`)

- `i18n/locales.json` — NEW. Array of `{ "tag": "en", "displayName": "English" }` objects. Ships with `en` and `fr-FR` (latter added in wave 14h).
- `i18n/en.json` — NEW. Source-of-truth catalog. Flat key→string map. Includes `enum.*` keys for every enum in `FestivalRider.Models`. Authored alongside the page-by-page UI migration.
- `i18n/fr-fr.json` — NEW (wave 14h). Mirrors `en.json` key set 1:1; values supplied by the user. The catalog parity test fails the build if any key is missing or any `{N}` placeholder count diverges.
- `i18n/AGENTS.md` — NEW. Rules: source-of-truth is `en.json`, parity test is gate, frozen-key rule, naming conventions for sections (`page.{name}.*`, `nav.*`, `toast.*`, `enum.*`, `print.{strategy}.*`).
- `js/i18n.js` — NEW. Two functions: `getNavigatorLanguage()` returns `navigator.language || ""`; `setHtmlLang(tag)` writes `document.documentElement.lang`.
- `index.html` — add `<script src="js/i18n.js"></script>` next to the existing JS files. Initial `<html lang="en">` stays; service rewrites at runtime.

### DI (`src/FestivalRider/Program.cs`)

- Register `services.AddScoped<ILocalizationService, LocalizationService>()`. NO migrator-pipeline registration (locale lives outside `AppState`).

### Tests (`tests/FestivalRider.Tests`)

- `LocalizationServiceTests.cs` — NEW. Cases: idempotent `EnsureLoadedAsync`; missing-`festivalrider.locale` key triggers `navigator.language` autodetect; persisted tag overrides autodetect; `SetLocaleAsync` raises `OnLocaleChanged` and persists; `SetLocaleAsync(currentTag)` is a no-op; `T(key)` returns English fallback for missing key in active catalog; `T(key)` returns formatted string with positional args; `Culture` matches the active tag. Uses `FakeJSRuntime`; uses an in-process `HttpClient` backed by an `HttpMessageHandler` stub serving fixture catalogs (no real network).
- `LocalizationCatalogTests.cs` — NEW. Reads every `*.json` linked from `src/FestivalRider/wwwroot/i18n/` via the test csproj `<Content>` link (per [Testing](#testing) below). Asserts: (a) every catalog is valid JSON of `Dictionary<string, string>`; (b) every non-English catalog's key set equals `en.json`'s; (c) for every shared key, the `{N}` placeholder count matches; (d) every public enum value under `FestivalRider.Models` has a key `enum.{TypeName}.{ValueName}` in `en.json`; (e) every locale listed in `locales.json` has a corresponding `{tag}.json` and vice-versa.
- `InvariantCsvUnderForeignCultureTests.cs` — NEW. Wraps `ExportServiceTests`'s round-trip cases inside `using (new ScopedCulture(CultureInfo.GetCultureInfo("de-DE"))) { ... }`. Asserts byte-for-byte equality with the Invariant baseline. Pins decimal-comma / date-format regressions.
- `BandRiderPrintStrategyTests.cs` (existing — `PrintStrategyTests.cs`) — add a case asserting that under `nl-BE` (mocked via `ILocalizationService` returning `nl-BE`), date rendering uses the culture's short-date format and section headings come from the catalog. Print strategy is constructed with a fake `ILocalizationService` exposing a small in-memory dictionary.
- `FestivalRider.Tests.csproj` — add `<ItemGroup><Content Include="..\..\src\FestivalRider\wwwroot\i18n\**\*.json" Link="i18n\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" /></ItemGroup>` so catalog tests resolve actual shipped files.

### Plans / docs (`Docs/Plans`)

- `readme.md` — add a row for 014.
- `001-initial-plan.md` — leave status unchanged. The "Validation: DataAnnotations + ValidationSummary" decision keeps validation messages in EN at the framework level; this plan flags localization of validation as future work in Out of scope.

### Root config

- `AGENTS.md` (root) — add a `## Localization` section with the locked rules: catalog location, source-of-truth, frozen keys, parity test, missing-key fallback, Invariant-culture wire format, `festivalrider.locale` key (separate from `AppState`), no migrator participation, component layering preserved.

## Catalog key conventions

Locked naming so reviewers don't bikeshed:

- `nav.{item}` — primary nav items (`nav.bands`, `nav.runningOrder`, `nav.settings`).
- `page.{name}.title` — `<PageTitle>` content (`page.bands.title`, `page.settings.title`).
- `page.{name}.{section}.{label}` — page-scoped headings, buttons, helper text.
- `section.{name}.title` — `<RiderSection>` titles (`section.contacts.title`, `section.travelParty.title`).
- `toast.{producer}.{kind}` — toast messages (`toast.storage.restored`, `toast.bundle.merged`, `toast.migration.applied`).
- `confirm.{action}.{field}` — `<ConfirmDialog>` content (`confirm.deleteBand.title`, `confirm.deleteBand.message`).
- `enum.{TypeName}.{ValueName}` — enum values (`enum.ContactRole.TourManager`).
- `print.{strategyKey}.{label}` — print-strategy labels (`print.band.contactsHeading`, `print.stage.startTimeColumn`).
- `banner.{name}` / `update.{label}` — banners and the update toast.
- `error.{name}` — generic error messages used across producers.
- `format.dateLong`, `format.dateShort` — reserved for date format strings only when a culture's default is unsuitable (rare; default to `Culture.DateTimeFormat`).

## Task order

Each step leaves the app compiling, runnable, and demoable.

### Implementation

1. **Framework + DI, no UI changes.** Add `ILocalizationService`, `LocaleDescriptor`, `LocalizationService`, `wwwroot/i18n/locales.json`, `wwwroot/i18n/en.json` (initially empty `{}`), `wwwroot/js/i18n.js`. Register in `Program.cs`. `MainLayout` calls `EnsureLoadedAsync` before `Storage.EnsureLoadedAsync`. No call sites use `T()` yet. Build green; behavior unchanged.
2. **Layout + nav.** `NavMenu` consumes `T("nav.*")`; render `<LocalePicker>`. `MainLayout` passes translated `Message` to `MultiTabBanner` and translated labels to `UpdateAvailableToast`. Demoable: nav labels swap when locale changes (English-only catalog so no visible swap yet, but `OnLocaleChanged` round-trip works end-to-end).
3. **Pages.** Migrate `BandList`, `RiderEditor`, `RunningOrder`, `Settings`, `RiderPrint`, `Counter`, `Weather`. Replace every literal. Add the corresponding keys to `en.json`. Pages subscribe to `OnLocaleChanged`.
4. **Components.** Promote literals in `BandCard`, `MultiTabBanner`, `UpdateAvailableToast`, `ShowPicker` to `[Parameter]`. Parents supply translated values. Add `LocalePicker.razor`.
5. **Print strategies + enum keys.** Strategies inject `ILocalizationService`, replace every label, render dates via `Culture`. Add every `enum.{TypeName}.{ValueName}` key to `en.json`.
6. **Service toasts.** `StorageService` and `BundleService` build toast / warning text via `T(key, args)`. `Settings.razor` + `RiderEditor.razor` + `RunningOrder.razor` likewise.
7. **AGENTS hygiene.** Add `wwwroot/i18n/AGENTS.md`. Update root `AGENTS.md` with the new `## Localization` section.

### Revisit checkpoint

8. **Triage post-impl.** Walk every page in EN and confirm zero string regressions vs. main. Lock the EN key set before any translation is authored. If the catalog grows past ~400 keys, revisit whether per-feature splits are worth a successor plan (default: NO — flat catalog is the lock).

### First translation

9. **`fr-FR` catalog (step 8 must be signed off first).** Add `wwwroot/i18n/fr-fr.json` with the full key set from `en.json` and translated values. Append the `fr-FR` entry to `wwwroot/i18n/locales.json`. Smoke-test by switching the picker and walking every page + every print route. The catalog parity test must pass green before merge.

### Tests

10. **Service unit tests.** `LocalizationServiceTests` — autodetect, persist, switch, fallback, idempotency.
11. **Catalog parity tests.** `LocalizationCatalogTests` — JSON validity, key-set parity, placeholder parity, enum coverage, `locales.json` ↔ `*.json` symmetry.
12. **Invariance regression test.** `InvariantCsvUnderForeignCultureTests` — round-trip CSV under `de-DE`, assert byte parity with Invariant baseline.
13. **Print-strategy locale test.** Extend `PrintStrategyTests` to assert culture-aware date rendering and catalog-sourced labels under a fake `fr-FR` localization.

### Docs

14. **Status & index sync.** Confirm 014 is `Active`, the index row in `Docs/Plans/readme.md` reflects `en` + `fr-FR`, and root `AGENTS.md` reflects the `## Localization` section.

## Implementation cadence

- **Wave 14a — Framework (step 1).** Demoable: build green, no UI change, scaffolding ready, autodetect + persist round-trips through devtools.
- **Wave 14b — Layout + nav (step 2).** Demoable: locale picker visible, persists across reload.
- **Wave 14c — Pages (step 3).** Demoable: every page reads from the catalog; behavior unchanged in EN.
- **Wave 14d — Components (step 4).** Demoable: dumb components stay dumb; parents thread translated values.
- **Wave 14e — Print + enums (step 5).** Demoable: print pages use culture-shaped dates; enum dropdowns and print labels read from catalog.
- **Wave 14f — Toasts (step 6).** Demoable: every toast and bundle warning sourced from catalog.
- **Wave 14g — AGENTS hygiene + triage (steps 7–8).** Reviewable as docs-only + EN catalog walkthrough; locks the EN key set before any translation begins.
- **Wave 14h — `fr-FR` catalog (step 9).** Demoable: switching the locale picker to `Français` swaps every label, every print heading, every toast, every enum.
- **Wave 14i — Tests (steps 10–13).** Reviewable as a green suite expansion.
- **Wave 14j — Docs sync (step 14).** Index + AGENTS sync.

## Out of scope

- **Translating user-typed content** (band notes, contact names, dietary restrictions). Always rendered as-stored.
- **Translating CSV column headers, bundle manifest field names, or `AppState` JSON property names.** Frozen wire format per AGENTS.
- **Localizing `DataAnnotations` validation messages** (`[Required]`, `[EmailAddress]`, `[Range]`). Default English messages stay; a successor plan can wire `ErrorMessageResourceName` against the catalog if user demand surfaces.
- **RTL languages** (Arabic, Hebrew). Bootstrap 5 RTL stylesheet + bidi-aware print CSS is a follow-up.
- **ICU MessageFormat / pluralization rules** beyond `"{0} band(s)"` style.
- **Per-print-route locale override** (`/print/{key}/{id}?locale=fr-BE`). Convenient for foreign-engineer PDFs but defer.
- **Currency / unit formatting.** Domain has no money. Distances stay metric, rendered as `Nm` literally.
- **Live cross-device locale sync.** Out of scope for the same reason cross-tab sync is.
- **Lazy / chunked catalog loading.** Catalogs are small; one fetch is fine. Revisit only if a single locale exceeds ~50 KB gzipped.

## Risks & migrations

- **Catalog drift.** `LocalizationCatalogTests` is the gate. Adding a key without updating non-English catalogs breaks the build. Mitigation: failure is loud and local.
- **Missing-key leakage.** Fallback returns English (NEVER raw key), so a missing `nl-BE` translation looks like an EN string instead of `enum.ContactRole.TourManager`. Acceptable degradation; the parity test prevents it shipping.
- **`navigator.language` mismatch.** Browser returns `en-US` while only `en` is available. Mitigation: language-only fallback in the resolution chain.
- **Service-worker cache staleness.** Adding / editing a catalog requires a service-worker cache bust. The published `service-worker-assets.js` hash already covers `wwwroot/i18n/*.json` since it covers all `wwwroot/**`. NO additional mitigation.
- **Locale persisted but no longer available.** A user uninstalls / removes a locale's `{tag}.json`; the persisted tag points at nothing. Mitigation: on `EnsureLoadedAsync`, if `festivalrider.locale` value is missing from `AvailableLocales`, fall through the resolution chain (language-only → `en`) and re-persist the resolved tag.
- **CurrentCulture leak into CSV.** A future reviewer adding `decimal.ToString()` without `InvariantCulture` would silently break round-trip in `de-DE`. `InvariantCsvUnderForeignCultureTests` catches the regression. Root AGENTS rule documents the policy.
- **Print-strategy date format ambiguity.** `nl-BE` short date is `d/M/yyyy`; `en-US` is `M/d/yyyy`. Print HTML is per-render shaped to the active locale; CSV stays ISO. Both producers are covered by tests.
- **Frozen-key discipline.** Removing or renaming a released key would silently lose translations on next deploy. Mitigation: AGENTS rule + reviewer enforcement + the "ship both old and new for one release, then drop" migration pattern documented in `wwwroot/i18n/AGENTS.md`.
- **Component layering pressure.** Some components (e.g. `BandCard`) gain four `[Parameter]` strings, which is verbose. Acceptable trade-off vs. breaking the existing components rule. Successor plan may introduce a `[CascadingParameter] ILocalizationService` if verbosity becomes a real cost; not now.
- **`fr-FR` translation quality.** Translations are user-provided. The parity test catches structural drift but cannot catch wrong-but-syntactically-valid copy. Mitigation: wave 14h is gated on a manual walk through every page and every print route under the active locale before merge.
