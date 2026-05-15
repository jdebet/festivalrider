# 020 — Running order schedule refactor

## Status

Draft

## Context

Successor to [019-show-scoped-bands.md](./019-show-scoped-bands.md). Plan 019 made shows self-contained (bands, running orders, stages) but left the running order as a simple table of `StartTime`/`SetLength`/`Changeover` records with no automatic timing logic. Real-world usage requires two distinct workflows: a traditional venue timeline (soundchecks packed before doors, sequential on-stage times) and a festival timeline (interleaved stages, early soundchecks, independent stage use). This plan replaces the flat `RunningOrderSlot` record with a rich event-chain model, introduces a dual-mode scheduler, and adds an interactive Gantt + side-panel UI.

## Decisions (locked)

- **Dual schedule mode** — `RunningOrder` carries `ScheduleMode` (`Traditional` | `Festival`). Two scheduler algorithms share the same data model but behave differently. No subclassing of `ShowData` or `RunningOrder`; mode-specific config lives in nullable child objects (`VenueTimingOptions`, `FestivalTimingTemplate`).
- **Event chain template** — Festival mode uses a per-`RunningOrder` `FestivalTimingTemplate` that lists `TimingEventType` entries in order with default durations. The user can reorder, add, remove, and rename entries. Multiple entries of the same type are allowed.
- **Anchor event** — Festival mode defaults to `ON_STAGE` as the anchor from which chains grow backward/forward, but any `TimingEventType` can be set as the anchor per `RunningOrder` (overrideable from show-level default). Traditional mode always anchors on `ON_STAGE`.
- **Cross-day `DateTime`** — All timing fields use `DateTime` (not `TimeOnly`). Each `RunningOrder` has an implicit `BaseDate = ShowData.DateOfOpening.AddDays(ShowDayNumber - 1)`. Times after midnight display with `+1d`, `+2d`, etc.
- **First show time separate from doors** — `ShowData` gains `FirstShowTime` (when first band goes on stage) distinct from `DoorsOpeningTime` (when public enters). Traditional mode uses `FirstShowTime` as the first band's `OnStageTime`. Festival mode uses it as the default anchor. If `EffectiveFirstShow` is null at both show and RO level, the scheduler falls back to `BaseDate.AddHours(20)` (8:00 PM) as a sensible default and emits a `FirstShowTimeMissing` warning.
- **Custom event display names** — `TimingChainEntry.CustomDisplayName` allows user-defined labels (e.g., renaming `SOUNDCHECK` to "Linecheck"). The scheduler always uses the enum value for logic; the label is UI-only.
- **On-stage sequence** — `CHANGEOVER` ends, then `PRESHOW_LINECHECK`, then `ON_STAGE`. `BACKSTAGE` time overlaps the previous band's set (5–10 min before previous ends). For the first band (no previous), `BackstageTime = OnStageTime - BackstageLeadMinutes`. It is a derived notification time, not a template event.
- **Soundcheck packing in traditional mode** — Soundchecks are packed backward from a deadline (`min(Doors, FirstShow) - BreakTime`) in `SoundcheckOrderIndex` order (default = reverse of playing order, overrideable per slot). Consecutive soundchecks on the same stage are separated by `SoundcheckGapMinutes` (default 0). Festival mode does not auto-pack soundchecks; they are user-placed or derived from the template chain.
- **Early soundcheck as a separate chain** — Festival headliners may have an `EarlyChain` (morning soundcheck, load-in, then load-out, then re-enter for main show). The scheduler validates `EarlyChain.End <= OnStageTime` on the same stage.
- **Stage linking** — `ShowData.StageLinkGroups` groups stages that share overlap constraints. `StageLinkConstraint.All` checks all overlaps across linked stages; `OnStageOnly` checks only on-stage overlaps. Schema is in place now; UI enforcement ships in a successor plan if scope grows.
- **No subclassing** — `VenueTimingOptions` and `FestivalTimingTemplate` are flat nullable properties on `RunningOrder`. Subclassing would break CSV round-trip, bundle migration, and `System.Text.Json` serialization in Blazor WASM.
- **Schema bump** — `AppState.SchemaVersion` 5 → 6. `V5ToV6Migrator` and `V5ToV6BundleMigrator` convert legacy `RunningOrderSlot` records.

## Open questions

None.

## Architecture rules

- `TimingEventType` MUST use `MACRO_CASE`. Values: `GET_IN`, `LOAD_IN_VENUE`, `LOAD_IN_STAGE`, `BACKSTAGE_DROP`, `CATERING`, `SETUP_ON_STAGE`, `SOUNDCHECK`, `CHANGEOVER`, `PRESHOW_LINECHECK`, `ON_STAGE`, `LOAD_OUT_STAGING`, `LOAD_OUT_VENUE`, `BACKSTAGE_WAIT`.
- `FREETIME` MUST NOT exist as an enum value; empty gaps on the Gantt are UI-only.
- `RunningOrderSlot` MUST be a mutable `class { get; set; }` (not a `record`) per the global mutability rule for UI-bound entities.
- `BandScheduleFlags` MUST contain only `None = 0` and `HasPersonalBackstageCurfew = 1`. `Headliner`, `EarlySoundcheck`, and `NoSoundcheck` MUST NOT exist as flags. Early soundcheck is signaled by `Slot.EarlyChain.Count > 0`. No-soundcheck is signaled by absence of a `SOUNDCHECK` entry in `PreShowEvents`.
- `RunningOrderScheduler` MUST be the sole service that computes timing chains. `BandService` MUST NOT contain scheduler logic.
- `RunningOrderScheduler` MUST emit `ScheduleWarning` objects for every constraint violation. The UI MUST display warnings as toasts/badges. The user MAY click "Allow overlap" to downgrade a warning to informational via `UserOverrideFlags`.
- `SlotTimingEvent`, `TimingChainEntry`, `FestivalTimingTemplate`, `VenueTimingOptions`, and `StageLinkGroup` MUST be mutable `class { get; set; }` (not `record`) because they contain mutable collections and are mutated via UI.
- `BandService.AddRunningOrder` MUST set `order.ShowId = ActiveShowId` before adding.
- `BandService.UpdateRunningOrder` MUST replace the entire `RunningOrder` object (including its `Slots` list) in the active show's `RunningOrders`.
- `BandService.UpdateShow` MUST copy ALL `ShowData` scalar fields into the existing show record, both existing (`TechnicalGetInTime`, `DoorsOpeningTime`, `SoundCurfewTime`, `BackstageCurfewTime`) and new (`DefaultScheduleMode`, `DefaultAnchorEvent`, `FirstShowTime`, `BreakTimeMinutes`, `SoundcheckGapMinutes`). Preserving only the old scalar set will silently wipe schedule configuration on show edit.
- `IRunningOrderScheduler` MUST be registered as `Scoped` in `Program.cs`.
- All persisted numeric/date conversions MUST use `CultureInfo.InvariantCulture`.
- Every new localization key MUST ship in `en.json` and `fr-fr.json` in the same commit, and MUST have a corresponding constant in `LocalizationKeys.cs`.
- `enum.ScheduleWarningType.{ValueName}` localization keys MUST be added alongside `TimingEventType` keys.
- `enum.ScheduleMode.{ValueName}` localization keys MUST be added for the mode selector dropdown.

## File-by-file scope

### Models (`src/FestivalRider/Models`)

- `TimeSlot.cs` — `Start` (DateTime), optional `End` (DateTime?). End=null means point-in-time.
- `TimingEventType.cs` — `MACRO_CASE` enum with 13 values. No `FREETIME`.
- `ScheduleMode.cs` — enum `Traditional`, `Festival`.
- `StageLinkGroup.cs` — `Guid Id`, `List<int> StageIds`, `StageLinkConstraint Constraint`.
- `StageLinkConstraint.cs` — enum `All`, `OnStageOnly`.
- `TimingChainEntry.cs` — `TimingEventType EventType`, `string? CustomDisplayName`, `int DefaultDurationMinutes`, `bool IsOptional`.
- `FestivalTimingTemplate.cs` — `List<TimingChainEntry> EarlyChain`, `List<TimingChainEntry> PreShowEntries` (reverse chronological), `List<TimingChainEntry> PostShowEntries` (chronological), `int DefaultSetLengthMinutes`.
- `VenueTimingOptions.cs` — booleans for which events exist (`IncludeGetIn`, `IncludeVenueLoadIn`, `IncludeStageLoadIn`, etc.) plus default duration fields (`DefaultGetInMinutes`, `DefaultSetLengthMinutes`, etc.). `IncludeStageLoadIn` controls whether `LOAD_IN_STAGE` appears in the traditional pre-show chain. `LOAD_IN_VENUE` is NOT part of the traditional chain; it only appears in festival early chains.
- `SlotTimingEvent.cs` — `TimingEventType EventType`, `DateTime? StartTime`, `int? DurationMinutes`, `bool IsPinned`.
- `RunningOrderSlot.cs` — replaces the `record`. `Guid BandId`, `int StageId`, `DateTime? OnStageTime`, `bool IsOnStagePinned`, `int? SetLengthMinutes`, `int? ChangeoverMinutes` (canonical duration for the on-stage forward cascade; the side panel edits THIS property, and the `PreShowEvents` CHANGEOVER entry reads from it — never stores its own copy), `int? PreShowLinecheckMinutes` (canonical duration for linecheck in the forward cascade; same single-source-of-truth rule as ChangeoverMinutes), `int SoundcheckOrderIndex` (controls traditional-mode soundcheck packing order; default = reverse of playing order, overrideable), `List<SlotTimingEvent> EarlyChain`, `List<SlotTimingEvent> PreShowEvents`, `List<SlotTimingEvent> PostShowEvents`, `DateTime? BackstageTime`, `int? BackstageLeadMinutes`, `bool IsBackstageTimePinned`, `DateTime? BackstageCurfewTime`, `bool IsBackstageCurfewPinned`, `BandScheduleFlags Flags`, `UserOverrideFlags OverrideFlags`, `string? Notes`.
- `RunningOrder.cs` — adds `ScheduleMode? ModeOverride`, `TimingEventType? AnchorEventOverride`, `DateTime? FirstShowTimeOverride`, `DateTime? BackstageCurfewTimeOverride`, `int? BreakTimeMinutesOverride`, `int? SoundcheckGapMinutesOverride`, `VenueTimingOptions? VenueOptions`, `FestivalTimingTemplate? FestivalTemplate`, `List<RunningOrderSlot> Slots`.
- `ShowData.cs` — adds `ScheduleMode DefaultScheduleMode`, `TimingEventType DefaultAnchorEvent`, `DateTime? FirstShowTime`, `DateTime? BackstageCurfewTime`, `int BreakTimeMinutes`, `int SoundcheckGapMinutes`, `List<StageLinkGroup> StageLinkGroups`.
- `BandScheduleFlags.cs` — `[Flags]` enum: `None = 0`, `HasPersonalBackstageCurfew = 1`.
- `UserOverrideFlags.cs` — `[Flags]` enum: `None = 0`, `AllowSoundcheckOverlap = 1`, `AllowOnStageOverlap = 2`.
- `ScheduleWarning.cs` — `ScheduleWarningType Type`, `string Message`, `int? SlotIndex`, `int? RelatedSlotIndex`.
- `ScheduleWarningType.cs` — enum: `BreakTimeViolation`, `SoundcheckBlockOverlap`, `OnStageOverlap`, `BackwardLockConflict`, `BarrierConflict`, `CateringOutsideHours`, `CurfewViolation`, `SoundcheckShrunk`, `SoundcheckOrderOverlap`, `UserOverrideOverlap`, `EarlySoundcheckAfterOnStage`, `ConstraintViolation`, `FirstShowTimeMissing`.
- `ScheduleResult.cs` — `bool Success`, `List<ScheduleWarning> Warnings`.
- `SlotEditRequest.cs` — `Guid SlotBandId`, `TimingEventType EventType`, `DateTime? NewStartTime`, `bool IsPinnedChange`. Carries a Gantt drag/edit action back to the page.
- `AppState.cs` — bump `SchemaVersion` default to 6.

### Services (`src/FestivalRider/Services`)

- `IRunningOrderScheduler.cs` — `ScheduleResult Recalculate(RunningOrder, ShowData)`, `ScheduleResult AddBand(...)`, `ScheduleResult RemoveBand(...)`, `ScheduleResult MoveBand(...)`, `List<ScheduleWarning> Validate(RunningOrder, ShowData)`.
- `RunningOrderScheduler.cs` — MUST inject `ILogger<RunningOrderScheduler>`. Implements both `Traditional` and `Festival` algorithms. Traditional: backward soundcheck packing from deadline, forward on-stage cascade, per-stage constraints. Festival: template-based chain growth from anchor, independent early chains, same-stage-only overlap checks.
- `BandService.cs` — `UpdateShow` MUST copy all `ShowData` scalar fields (existing + new) into the existing show record. `DeleteBand` MUST remove any `RunningOrderSlot` referencing the deleted band across the same show's running orders (logic unchanged, but operates on the new slot shape).
- `BandPlacement.cs` — `Guid BandId`, `int StageId`, `int? InsertAtIndex`, `DateTime? PinnedOnStageTime`.
- `Program.cs` — register `IRunningOrderScheduler` as `Scoped`.

### Pages (`src/FestivalRider/Pages`)

- `RunningOrderV2.razor` — Orchestrator. Mode selector, top toolbar (doors, first show, curfew, break time, soundcheck gap, backstage curfew), hosts Gantt + side panel + template editor. Emits warning toasts.

### Components (`src/FestivalRider/Components`)

- `ScheduleGantt.razor` — Horizontal timeline. Color-coded bars per event type. Cross-day awareness (`+1d` badges). Click-to-select, drag-to-move (respects pins). Drag-to-move supports crossing midnight; day offset is recalculated from `BaseDate` and `+1d` badges update dynamically. Emits `EventCallback<SlotEditRequest>`.
- `ScheduleBandPanel.razor` — Side panel. Three sections: Early Chain, Pre-Show Chain, Post-Show Chain. Each shows Event | Time | Duration | Pinned. Read-only `BackstageTime` row. `Flags` checkboxes. Per-band `BackstageCurfewTime` input.
- `TemplateEditor.razor` — Modal for editing `FestivalTimingTemplate`. Drag-to-reorder entries. Set default durations. Toggle optional. Rename display labels. Preset buttons: "Festival Main Stage", "Festival Tent", "Traditional Venue".
- `VenueOptionsEditor.razor` — Panel for traditional mode: checkboxes for included events, default durations.

### Migrators (`src/FestivalRider/Migrators`)

- `V5ToV6Migrator.cs` — Converts legacy `RunningOrderSlot` (StartTime, SetLengthMinutes, ChangeoverMinutes, Notes) to new model. Sets `Mode = Traditional`, populates `VenueOptions` with defaults. Maps `StartTime` → `OnStageTime` (pinned). Maps `ChangeoverMinutes` → `Slot.ChangeoverMinutes` (top-level canonical property) AND creates a corresponding `PreShowEvents` CHANGEOVER entry that reads from it. Sets `PreShowLinecheckMinutes` to `VenueTimingOptions.DefaultPreShowLinecheckMinutes` default (10). Sets `SoundcheckOrderIndex` to reverse playing order. Sets `SoundcheckGapMinutes` to 0.

### BundleMigrators (`src/FestivalRider/BundleMigrators`)

- `V5ToV6BundleMigrator.cs` — Same conversion logic as `V5ToV6Migrator` but for bundle manifest format.

### Localization (`src/FestivalRider/wwwroot/i18n`)

- `en.json` — New keys for all UI labels, warning messages, enum localization (`enum.TimingEventType.GET_IN`, etc.), preset names.
- `fr-fr.json` — Parity with `en.json`.
- `LocalizationKeys.cs` — New constants for every added key.

## Task order

1. Add new models (`TimingEventType`, `ScheduleMode`, `StageLinkGroup`, `TimingChainEntry`, `FestivalTimingTemplate`, `VenueTimingOptions`, `SlotTimingEvent`, `BandScheduleFlags`, `UserOverrideFlags`, `ScheduleWarning`, `ScheduleWarningType`, `ScheduleResult`). Update `RunningOrder`, `RunningOrderSlot`, `ShowData`, `AppState` (bump schema). Update all consumers of `RunningOrderSlot`: `RunningOrderSlotRow.razor`, `RunningOrderV2.razor`, `ExportService`, `BundleService`, `StagePrintStrategy`, `RolePrintStrategy`, and all existing tests. App compiles.
2. Write `IRunningOrderScheduler` interface and `RunningOrderScheduler` with `Traditional` mode algorithm. Register in `Program.cs`. Add unit tests for traditional mode. App compiles, tests pass.
3. Write `V5ToV6Migrator` and `V5ToV6BundleMigrator`. Add migration tests. Bump `AppState.SchemaVersion` to 6. App compiles, tests pass.
4. Write `FestivalTimingTemplate` algorithm in `RunningOrderScheduler`. Add unit tests for festival mode. App compiles, tests pass.
5. Add localization keys for all new UI text. Update `LocalizationKeys.cs`. Add `LocalizationCatalogTests` coverage. App compiles, tests pass.
6. Create `ScheduleGantt.razor`, `ScheduleBandPanel.razor`, `TemplateEditor.razor`, `VenueOptionsEditor.razor`. Wire into `RunningOrderV2.razor`. App compiles, runnable.
7. Update CSV writer/reader for new `RunningOrderSlot` columns. Update `ExportServiceTests` for byte stability. App compiles, tests pass.
8. Update print strategies that consume `RunningOrderSlot` to use the new model (no functional change to printed output yet; just field mapping). App compiles.
9. Add preset templates ("Festival Main Stage", "Festival Tent", "Traditional Venue") to `TemplateEditor.razor`. App compiles, runnable.
10. Full integration test: create a show, add bands, switch modes, add slots, verify Gantt and warnings. Commit.

## Implementation cadence

- **Wave 1 — Models and scheduler (Traditional)**: tasks 1–2. Demoable: create a traditional running order, add bands, see auto-computed soundcheck and on-stage times with warnings.
- **Wave 2 — Migration and persistence**: tasks 3. Demoable: export/import a v5 bundle, load in v6 app, verify migrated slots.
- **Wave 3 — Festival mode and UI**: tasks 4–6. Demoable: switch to festival mode, configure a template, add bands with early soundchecks, see Gantt with color-coded bars.
- **Wave 4 — CSV, print, polish**: tasks 7–10. Demoable: export running order CSV, print stage schedule, verify byte-stable round-trip.

## Out of scope

- Stage linking UI and enforcement (`StageLinkGroups` schema is in place; UI enforcement deferred to successor plan).
- Auto-packing soundchecks into gaps between on-stage times in festival mode (user-placed only for v1).
- Drag-to-resize duration on the Gantt (v1 supports drag-to-move only; duration edits via side panel).
- Live cross-tab sync of running order edits.
- PDF export of the Gantt chart (print strategies continue to emit text tables).

## Risks & migrations

- **Risk: legacy `RunningOrderSlot` is an immutable `record`** — the new model is a mutable `class`. The v5→v6 migrator must convert every legacy record into a new `class` instance with correct defaults. Mitigation: thorough migrator tests covering edge cases (empty slots, missing notes, pinned/unpinned semantics).
- **Risk: CSV format change breaks external tools** — new columns are added to the running-order CSV. Mitigation: add columns in declaration order, update writer/reader/tests together, ensure byte stability.
- **Risk: `DateTime` conversion breaks existing `TimeOnly` consumers** — `ExportService`, print strategies, and the old `RunningOrderSlotRow` component read `TimeOnly`. Mitigation: convert at the boundary (read `DateTime.TimeOfDay` for same-day, display `+1d` for cross-day). Update all consumers in the same commit.
- **Risk: dual-mode logic complexity in one scheduler** — `RunningOrderScheduler` carries two algorithms. Mitigation: private methods clearly named `ComputeTraditionalTimeline` and `ComputeFestivalTimeline`, with shared helper methods for overlap checks and constraint validation.
- **Risk: `StageLinkGroups` orphan references** — deleting a stage does not auto-remove its ID from link groups. Orphaned IDs are harmless (scheduler skips non-existent stages) but may produce stale warnings. Auto-cleanup deferred to a successor plan.
- **Risk: `TimeSlot` date anchoring** — catering `TimeSlot` uses absolute `DateTime`. Changing `ShowData.DateOfOpening` or `ShowDayNumber` does not automatically shift catering times. The UI displays them as times only; the user must adjust them manually when the show date changes.
