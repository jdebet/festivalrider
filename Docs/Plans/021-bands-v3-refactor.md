# Bands V3 Refactor — Glanceable Grid, Model Extensions, Auto-Save

Refactor the `/bands-v3` grid into a glanceable, mostly read-only table with reusable detail pop-ups, extend the domain model (Band/Lighting/Power/Monitors/Stage + running-order timing), bump the persisted schema 6 → 7 with matching migrators, and add a 5s UI-level auto-commit while editing.

## Confirmed decisions (from clarifications)

- **Auto-save** = UI-level auto-commit: edits persist ~5s after typing stops, **without** needing to blur. The 1s `StorageService` write debounce stays unchanged (no AGENTS.md amendment).
- **Detail pop-ups are read-only.** Refactored section columns show tallies/glanceable data only; full editing happens in the band detail editor (`RiderEditorV3`). The pop-up is a reusable component.
- **`Band.DayPlaying`** is `>= 1` and capped at the owning show's `ShowDayCount` (custom validation; can't be a static `[Range]`).
- **`Power Outlets`** lives on `StageSetup` (shown under the grid's **Stage** group).

## Prerequisite: successor Docs/Plan

This change adds NuGet-free model/persistence changes and bumps the schema, which touches `## Decisions (locked)` territory. **First implementation step must add `Docs/Plans/021-bands-v3-refactor.md`** (status `Active`, referencing 015/017/019/020), and update `Docs/Plans/readme.md` index. Capture: schema bump 6→7, the new CSV section/key additions, and the new enum members. No AGENTS.md edits are required given the auto-save decision above.

## Model changes (`src/FestivalRider/Models`, one concept per file)

- **`LightingRig.cs`** — add `bool HasBackdrop`. When `false`, the editor hides/zeroes `BackdropWidthMeters`/`BackdropHeightMeters`. Migration sets `HasBackdrop = true` when either dimension is non-null/non-zero.
- **`ContactRole.cs`** — append `Artist`, `LightEngineer`, `Production` (append after the existing `Other`; never reorder the survivors). **Remove `BackingTech`**; existing `BackingTech` data maps to `Other` in migration (see Persistence). Ship `enum.ContactRole.Artist` / `enum.ContactRole.LightEngineer` / `enum.ContactRole.Production` keys. The orphaned `enum.ContactRole.BackingTech` localization key (and its `LocalizationKeys` constant) MUST stay in the catalog — released keys are never removed.
- **`Band.cs`** — add `int DayPlaying { get; set; } = 1;`. Bounded `>= 1` and `<= owning ShowData.ShowDayCount` via a custom validator surfaced in the page/editor (not a static attribute, since the cap is dynamic).
- **`StageSetup.cs`** — add `string? PowerOutlets` (free text: outlet count, plug type, voltage…).
- **`AmbianceMics.cs`** — NEW model: `bool Present`, `int Count`, `CableProvider Provider`. Mutable `class` (UI-bound). Added to `MonitorSetup.cs` as `AmbianceMics AmbianceMics { get; set; } = new();`.
- **`InEarInputType.cs`** — NEW `[Flags]` `enum` (multi-select): `None = 0`, `XLR = 1`, `Jack635 = 2` (Jack 6.35 mm), `MiniJack35 = 4` (mini-jack 3.5 mm), `RCA = 8`, `Other = 16`. Explicit power-of-two values so several connectors can be selected at once. Ship `enum.InEarInputType.*` keys (no key for `None`).
- **`InEarMonitor.cs`** — replace the planned `string? InputType` with `InEarInputType InputType { get; set; }` as a **multi-select flags** field (**always visible/editable**, independent of `IsWireless`; several connectors may be combined). Add `string? InputTypeOther` (paired override, round-tripped when the `Other` flag is set) and `bool IsStereo` (true = stereo, false = mono). `Model`/`Frequency` remain for the wireless case.
- **`TimingEventType.cs`** — append `SETUP_BACKSTAGE` alongside `SETUP_ON_STAGE`. Ship `enum.TimingEventType.SETUP_BACKSTAGE`. Wire it into `RunningOrderScheduler`/`TemplateEditor` parallel to `SETUP_ON_STAGE` (selectable timing event; same fixed-duration treatment).

All additions are auto-property defaults per Models AGENTS rules; models stay data-only.

## Persistence (schema 6 → 7)

- **`AppState.cs`** — bump default `SchemaVersion` to `7`.
- **`StorageService.cs`** — bump `CurrentSchemaVersion` to `7` (constant only; debounce untouched).
- **`Migrators/V6ToV7Migrator.cs`** — NEW, `IStateMigrator` (pure `JsonNode`, no Models types). Per band: set `dayPlaying` default `1`; add `stage.powerOutlets` (null); add `monitors.ambianceMics` (`{present:false,count:0,provider:Venue}`); set `lighting.hasBackdrop` from existing backdrop dims; remap every contact `role == "BackingTech"` to `"Other"`; default each in-ear `inputType` to `""` (no flags set / `None`), `inputTypeOther` to `null`, and `isStereo` to `false`; leave the new `ContactRole` members to default deserialization. Register in `Program.cs`.
- **`BundleMigrators/V6ToV7BundleMigrator.cs`** — NEW, `IBundleMigrator`, mirrors the per-band CSV additions on the bundle scratch (adds the new rows/columns). Bump `BundleService` manifest `SchemaVersion` to `7`. Register in `Program.cs`.
- Released migrator files are frozen; these are new files only.

## CSV (`ExportService.cs` + `ExportServiceTests.cs`, byte-stable round-trip)

Add writer + reader + tests together for each:
- `Band` section: `DayPlaying`.
- `Tech.Lighting`: `HasBackdrop` (emit in declaration order, before backdrop dims).
- `Tech.Stage`: `PowerOutlets`.
- `Tech.Monitors.AmbianceMics`: new keys `Present`, `Count`, `Provider`.
- `Tech.InEar`: `InputType` (flags enum — round-trips as a comma-joined name list via the existing `FormatFlags`/`ParseFlags` helpers) + paired `InputTypeOther` override (round-trip the override only when the `Other` flag is set) + `IsStereo`.
- New `ContactRole`/`InEarInputType`/`TimingEventType` members round-trip via existing `ParseEnum` (verify in tests). Verify legacy `BackingTech` strings no longer appear in fresh exports (already remapped to `Other` by the migrator).
- Running-order CSV columns are unchanged by the new timing event (still enum string in existing column).

## UI — reusable read-only pop-up

- **`Components/DetailModal.razor`** — NEW reusable component. No service/JS/storage injection (Components AGENTS rule). Surface: `[Parameter] bool Show`, `[Parameter] string Title`, `[Parameter] RenderFragment ChildContent`, `[Parameter] EventCallback OnClose`. Bootstrap modal/overlay markup; closeable via button + backdrop. Renders read-only content supplied by the caller. All strings arrive pre-translated via parameters.

## UI — `Components/BandGridRow.razor` (the bulk of the work)

Per-section behavior. "Tally" cells are always read-only; an expand button opens `DetailModal` with the full read-only breakdown (composed from `BandGridLabels` strings). Remaining inline-editable fields keep the existing lock/unlock + `@onblur` pattern.

- **Notes columns (all groups):** render a 2-line clamped, ellipsised read-only view when locked (`-webkit-line-clamp:2`); the editable textarea appears when the row is unlocked. Applies to Band, FOH, Monitors, Stage, and Tech notes cells.
- **Contacts:** column shows only the first contact (role + name); expand → all contacts in pop-up. Read-only.
- **Travel Party:** one column per existing `PartyType` value (`BandMember`, `Tech`, `Production` — unchanged; the new roles live on `ContactRole`, not `PartyType`) showing the count tally; expand → full member list in pop-up.
- **Cabling:** one column per `CableType` value showing tally as `X` or `X + Y` where `Y` is the provider-`Brought` count rendered in red and the venue/standard count in default color; expand → full cable list.
- **Lighting:** Floor machines becomes a Yes/No glance (any machines?); expand → machine details. Backdrop shows the new Yes/No (`HasBackdrop`) and width/height inline-editable only when backdrop is on.
- **Power:** single glance cell rendering e.g. `16A` or `16 + 32T + 64T` (T suffix = three-phase); expand → amperage/phase/adapter/power-outlet detail. (Amperage/Phase currently a single value pair — render compactly; full breakdown in pop-up.)
- **Monitors:**
  - Wedge column: number of wedge entries, counting `DualLinked` or `Stereo` entries as 2; separate column for drumfill count.
  - IEM: two columns — number brought (`Provider == Brought`) and number needed/venue (`Provider == Venue`).
  - Ambiance mics surfaced in the Monitors pop-up (and a compact Yes/No glance).
  - Expand → full wedge/IEM/ambiance detail incl. per-IEM `InputType` (the full set of selected connectors + `Other` override) and stereo/mono, plus the wireless `Model`/`Frequency`.
- **Stage:** tally columns for Risers, Other Risers, Wireless Mics (counts); each expandable to its own pop-up detail. `PowerOutlets` (text) + `BringsOwnMics` (Yes/No) remain inline-editable.

Add the new add/remove handlers only where inline editing remains; remove inline editors for the now read-only sections (Contacts, Travel Party, Cabling list, Lighting floor machines, Monitors lists, Risers lists). Track expanded pop-up state with private fields (allowed transient UI state).

## UI — `Pages/BandListV3.razor`

- Rewrite the two header rows and the `grid-template-columns` template to match the new column set (per-`PartyType` columns, per-`CableType` columns, split Monitors columns, Stage tally columns, new Power glance, Backdrop Yes/No). Header group `span` counts updated accordingly.
- **5s auto-commit:** add a debounced commit (per-row `CancellationTokenSource` + `Task.Delay(5s)`, cancel-and-restart on each input) that calls `BandService.UpdateBand` without requiring blur. Wire grid row inputs to signal "dirty"; flush on blur/lock/dispose to avoid losing the tail edit. Keep `OnChange → StorageService` 1s write debounce intact.
- `DayPlaying` validation against active `ShowData.ShowDayCount`.

## UI — `Components/BandGridLabels.cs`

Add label fields for: new `ContactRole` members, `InEarInputType` members, per-`CableType` headers, Power glance, Backdrop Yes/No, Monitors split columns (brought/needed/drumfill), Ambiance mics, Stage tally headers, `PowerOutlets`, `InputType`, `DayPlaying`, and pop-up titles/section headings. Populate in `RebuildLabels()`.

## Editor — `Pages/RiderEditorV3.razor`

Surface every new field for full editing (since pop-ups are read-only): `DayPlaying`, `HasBackdrop` toggle gating backdrop dims, `PowerOutlets`, Ambiance mics (present/count/provider), per-IEM `InputType` (always-visible `InEarInputType` multi-select / checkbox group + `Other` override) and stereo/mono toggle, and the new `ContactRole` options. Use `EditForm` + `DataAnnotationsValidator` + `ValidationSummary`.

## Scheduler / Running order

- `RunningOrderScheduler.cs` + `TemplateEditor.razor` / event pickers: include `SETUP_BACKSTAGE` wherever `SETUP_ON_STAGE` is offered/handled (selectable event type, analogous duration handling). No schema change to `RunningOrder` itself beyond the enum member already covered by the schema bump.

## Localization (`wwwroot/i18n/en.json` + `LocalizationKeys.cs`)

Add keys (and 1:1 `LocalizationKeys` constants, plus `fr-fr.json` parity) for: new enum members (`enum.ContactRole.Artist`/`LightEngineer`/`Production`, `enum.InEarInputType.*`, `enum.TimingEventType.SETUP_BACKSTAGE`), new field labels (`field.lighting.hasBackdrop`, `field.stage.powerOutlets`, `field.band.dayPlaying`, `field.monitors.ambianceMics*`, `field.monitors.inEarInputType`, `field.monitors.inEarStereo`, `field.monitors.wedgeCount`/`drumfill`/`iemBrought`/`iemNeeded`, cable-type/power glance headers), pop-up titles, and the expand/close button labels. Never remove or renumber existing keys — the orphaned `enum.ContactRole.BackingTech` key stays (its enum member is gone, but released keys are never removed). `LocalizationCatalogTests` must stay green.

## Tests (`tests/FestivalRider.Tests/`)

- `ExportServiceTests` — round-trip all new CSV keys + new enum members (byte-stable).
- New migrator tests — `V6ToV7Migrator` (state) and `V6ToV7BundleMigrator` (bundle) default/derive correctly (incl. `BackingTech → Other` contact-role remap and the `inputType`/`isStereo` IEM defaults); full v6→v7 chain reaches current version.
- `LocalizationCatalogTests` — parity for new keys.
- Service tests via interfaces only; fake `IJSRuntime`/time; never touch real storage.

## Implementation cadence (waves)

1. **Plan + model + schema:** add `021` Docs plan; model changes; schema bump; `V6ToV7` state + bundle migrators + registrations. Build green; migration verified.
2. **Persistence/CSV:** `ExportService` writer/reader + `ExportServiceTests`; bundle manifest version.
3. **Reusable pop-up + grid read-only refactor:** `DetailModal.razor`, `BandGridRow.razor`, `BandGridLabels.cs`, `BandListV3.razor` columns; Notes clamping.
4. **Auto-save:** 5s debounced UI auto-commit in `BandListV3`/row.
5. **Editor + scheduler:** `RiderEditorV3` new fields; `SETUP_BACKSTAGE` wiring; localization keys; tests.

## Risks

- **Grid column template drift** — header `span`s, `grid-template-columns`, and `BandGridRow` cell order must stay byte-aligned; change them in lockstep.
- **DayPlaying dynamic cap** — can't be a static attribute; validate in page/editor and re-validate when `ShowDayCount` shrinks.
- **Enum ordinal stability** — append new members (`ContactRole.Artist`/`LightEngineer`/`Production`, `InEarInputType.*`) rather than reordering survivors. Removing `ContactRole.BackingTech` shifts the ordinals after it, but CSV/JSON store names and the `V6ToV7Migrator` remaps `BackingTech → Other`, so persisted data is safe; never reorder the remaining members.
- **Migrator coverage** — without `V6ToV7` (state) and bundle migrators, existing v6 data falls back to backup-and-reset / refused import. Both are mandatory in wave 1/2.
- **Notes clamp vs edit** — clamp only the locked read-only view; reveal the textarea on unlock to preserve inline editing.
