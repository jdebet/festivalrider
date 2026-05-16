# 020 — Running order schedule refactor (revision V2)

> WIP successor draft. Folds review feedback back into plan 020. Plan 020 is still `Draft`, so once this revision is accepted, the contents replace `Docs/Plans/020-running-order-schedule-refactor.md` in place. No new plan number is minted.

## Status

Draft

## Context

Successor to [019-show-scoped-bands.md](../Plans/019-show-scoped-bands.md). Plan 019 made shows self-contained (bands, running orders, stages) but left the running order as a flat table of `StartTime`/`SetLength`/`Changeover` records with no automatic timing logic. Real-world usage requires two distinct workflows: a traditional venue timeline (soundchecks packed before doors, sequential on-stage times) and a festival timeline (interleaved stages, early soundchecks, independent stage use). This plan replaces the immutable `RunningOrderSlot` record with a rich event-chain model, introduces a dual-mode scheduler, and adds an interactive Gantt + side-panel UI.

## Decisions (locked)

- **Dual schedule mode** — `RunningOrder` carries a `ScheduleMode? ModeOverride` resolved against `ShowData.DefaultScheduleMode`. Two scheduler algorithms share the same data model. No subclassing of `ShowData` or `RunningOrder`; mode-specific config lives in nullable child objects (`VenueTimingOptions`, `FestivalTimingTemplate`).
- **Event chain template** — Festival mode uses a per-`RunningOrder` `FestivalTimingTemplate` listing `TimingEventType` entries with default durations. The user can reorder, add, remove, and rename entries; multiple entries of the same type are allowed.
- **Anchor event** — Festival mode resolves anchor as `ro.AnchorEventOverride ?? show.DefaultAnchorEvent ?? ON_STAGE`. Traditional mode always anchors on `ON_STAGE`.
- **Cross-day `DateTime`** — All in-memory timing fields are `DateTime`. `BaseDate = ShowData.DateOfOpening.AddDays(ShowDayNumber - 1)`. Times after midnight render with `+1d`, `+2d`, … badges. CSV persists time-of-day separately from a small integer `DayOffset` column so cross-day data round-trips losslessly.
- **First show time separate from doors** — `ShowData` gains four nullable schedule scalars (`TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, `SoundCurfewTime`, plus `BackstageCurfewTime`). All four are net-new — none of them exist in the current `ShowData`. `RunningOrder` mirrors each with a nullable `*Override`. If `EffectiveFirstShow` is null at both levels, scheduler falls back to `BaseDate.AddHours(20)` and emits `FirstShowTimeMissing`.
- **Effective-value resolution rule** — for every show-level scalar X with a per-RO override, the effective value is `ro.XOverride ?? show.X`. Scheduler MUST consult only the effective value, never the raw underlying property.
- **Custom event display names** — `TimingChainEntry.CustomDisplayName` allows user-defined labels (e.g., renaming `SOUNDCHECK` to "Linecheck"). Scheduler uses the enum value for logic; the label is UI-only.
- **On-stage sequence** — `CHANGEOVER` ends, then `PRESHOW_LINECHECK`, then `ON_STAGE`. `BackstageTime` is by default derived (`OnStageTime - BackstageLeadMinutes` for the first band on a stage, `min(OnStageTime - BackstageLeadMinutes, PreShowLinecheckTime)` otherwise) but is also pin-able: when `IsBackstageTimePinned == true`, the stored `BackstageTime` value wins over derivation.
- **Per-band backstage curfew** — `Slot.BackstageCurfewTime` is consulted only when `Slot.Flags & BandScheduleFlags.HasPersonalBackstageCurfew != 0`. Otherwise the slot inherits `EffectiveBackstageCurfew` from the RO.
- **Soundcheck packing in traditional mode** — Soundchecks pack backward from `min(EffectiveDoors, EffectiveFirstShow) - EffectiveBreakTime` in `SoundcheckOrderIndex` order (default = reverse of playing order). Same-stage consecutive soundchecks are separated by `EffectiveSoundcheckGap`. Festival mode never auto-packs soundchecks.
- **Early soundcheck as a separate chain** — Festival headliners may have an `EarlyChain`. Scheduler validates `EarlyChain.End <= OnStageTime` on the same stage and emits `EarlySoundcheckAfterOnStage` on violation.
- **Stage linking schema-only** — `StageLinkGroups` schema lands now; scheduler MUST ignore it in v6. UI and enforcement are deferred to a successor plan.
- **`RunningOrderSlot.Id`** — slots gain `Guid Id` defaulted to `Guid.NewGuid()` so the Gantt, side panel, warnings, and edit requests can address slots by stable identity instead of list index. Migrators mint fresh Ids per slot.
- **Mutability override of AGENTS.md example** — root `AGENTS.md` lists `RunningOrderSlot` as the canonical immutable-record example. This plan converts it to a UI-mutated `class { get; set; }`. The plan-wins meta-rule applies.
- **Per-RO scalars and templates are JSON-only in bundles** — `RunningOrder.ModeOverride`, `AnchorEventOverride`, the four time overrides, `BreakTimeMinutesOverride`, `SoundcheckGapMinutesOverride`, `VenueOptions`, and `FestivalTemplate` persist only in `AppState` JSON. v6 bundle CSV does NOT round-trip them; they reset to defaults on bundle import. A successor plan may extend bundle wire format.
- **Catering and `TimeSlot` are out of scope** — the WIP source had `BreakfastHours`/`LunchHours`/`DinnerHours` and `CateringSlot`. Drop `TimeSlot.cs` and `ScheduleWarningType.CateringOutsideHours` from this plan. Defer to a successor.
- **Schema bump** — `AppState.SchemaVersion` 5 → 6. `V5ToV6Migrator` (state) and `V5ToV6BundleMigrator` (bundle) ship together.

## Open questions

None.

## Architecture rules

### Naming, mutability, registration

- `TimingEventType` MUST use `MACRO_CASE` (`GET_IN`, `LOAD_IN_VENUE`, `LOAD_IN_STAGE`, `BACKSTAGE_DROP`, `CATERING`, `SETUP_ON_STAGE`, `SOUNDCHECK`, `CHANGEOVER`, `PRESHOW_LINECHECK`, `ON_STAGE`, `LOAD_OUT_STAGING`, `LOAD_OUT_VENUE`, `BACKSTAGE_WAIT` — 13 values). Rationale: visual distinction from regular PascalCase enums signals "wire-format event tokens" used in CSV, JSON, and localization keys.
- `FREETIME` MUST NOT exist as an enum value; empty gaps on the Gantt are UI-only.
- `RunningOrderSlot`, `SlotTimingEvent`, `TimingChainEntry`, `FestivalTimingTemplate`, `VenueTimingOptions`, and `StageLinkGroup` MUST be mutable `class { get; set; }`.
- `BandScheduleFlags` MUST contain only `None = 0` and `HasPersonalBackstageCurfew = 1`. Early soundcheck is signaled by `Slot.EarlyChain.Count > 0`. No-soundcheck is signaled by absence of a `SOUNDCHECK` entry in `PreShowEvents`.
- `UserOverrideFlags` MUST contain `None = 0`, `AllowSoundcheckOverlap = 1`, `AllowOnStageOverlap = 2`.
- `IRunningOrderScheduler` MUST be registered as `Scoped` in `Program.cs`. `V5ToV6Migrator` MUST be registered as `IStateMigrator`. `V5ToV6BundleMigrator` MUST be registered as `IBundleMigrator`. All previous migrator registrations stay intact.

### Scheduler contract

- `IRunningOrderScheduler` MUST be the sole computer of timing chains. `BandService` MUST NOT contain scheduler logic.
- Scheduler methods mutate the passed `RunningOrder` graph in place. Pages MUST call `IBandService.UpdateRunningOrder(ro)` to commit. The "BandService is the sole `AppState` mutator" rule remains: the scheduler mutates a graph node, BandService commits it.
- Scheduler interface methods are `Recalculate`, `AddSlot`, `RemoveSlot`, `MoveSlot`, `SetSoundcheckOrder`, `Validate`. NEVER name them `AddBand`/`RemoveBand`/`MoveBand` (collision with `IBandService`).
- Scheduler MUST emit one `ScheduleWarning` per detected constraint violation. UI MUST display warnings as toasts/badges. The user MAY click "Allow overlap" to downgrade specific warnings via `Slot.OverrideFlags`; the warning then becomes informational (`UserOverrideOverlap`).
- Scheduler MUST ignore `StageLinkGroups` in v6. Same-stage overlap checks ship; cross-stage linking does not.

### Single-source-of-truth invariant

- `Slot.ChangeoverMinutes` and `Slot.PreShowLinecheckMinutes` are canonical durations for the on-stage forward cascade. Any `SlotTimingEvent` in `PreShowEvents` whose `EventType` is `CHANGEOVER` or `PRESHOW_LINECHECK` MUST leave `DurationMinutes = null`; the scheduler dynamically reads from the slot scalar at compute time. CSV writer/reader, JSON serializer, and migrators MUST preserve this invariant. Storing a duplicate copy is forbidden because it can drift.
- `SlotTimingEvent.IsPinned` refers to that event's start time only. Duration is either pinned via a non-null `DurationMinutes`, or derived from the slot scalar (CHANGEOVER, PRESHOW_LINECHECK), or derived from the template default. No drag-to-resize ships in v6.

### State and bundle

- `BandService.AddRunningOrder` MUST set `order.ShowId = ActiveShowId` before adding (already enforced).
- `BandService.UpdateRunningOrder` MUST replace the entire `RunningOrder` object including its `Slots`, `VenueOptions`, and `FestivalTemplate` references in the active show's `RunningOrders` (already enforced for `Slots`; extend wording to cover the new graph).
- `BandService.UpdateShow` MUST copy every new `ShowData` scalar (`DefaultScheduleMode`, `DefaultAnchorEvent`, `TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, `SoundCurfewTime`, `BackstageCurfewTime`, `BreakTimeMinutes`, `SoundcheckGapMinutes`) plus `StageLinkGroups` into the existing show record. Preserve nested `Bands` and `RunningOrders` references unchanged (per plan 019 rule).
- All persisted numeric/date conversions MUST use `CultureInfo.InvariantCulture`.

### Localization

- Every new localization key MUST ship in `en.json` and `fr-fr.json` in the same commit, with a corresponding constant in `LocalizationKeys.cs`. `LocalizationCatalogTests` parity MUST stay green.
- `enum.TimingEventType.{ValueName}`, `enum.ScheduleMode.{ValueName}`, `enum.ScheduleWarningType.{ValueName}` keys ship with the matching enum values.

## CSV format

This section is authoritative for the running-order CSV in v6 and amends the AGENTS.md root rule (`ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes`).

- Running-order CSV columns (locked, in declaration order):
  `Id,ShowId,Stage,BandName,OnStageTime,OnStageDayOffset,IsOnStagePinned,SetLengthMinutes,ChangeoverMinutes,PreShowLinecheckMinutes,SoundcheckOrderIndex,BackstageTime,BackstageDayOffset,IsBackstageTimePinned,BackstageLeadMinutes,BackstageCurfewTime,BackstageCurfewDayOffset,IsBackstageCurfewPinned,Flags,OverrideFlags,Notes`
- `OnStageTime`, `BackstageTime`, `BackstageCurfewTime` use `HH:mm` (`CultureInfo.InvariantCulture`). The paired `*DayOffset` column is an integer (`0` = same day, `1` = next day, etc.). Empty time string + offset `0` means null.
- `Flags` and `OverrideFlags` are emitted as comma-separated enum value names (e.g., `HasPersonalBackstageCurfew`). Empty string means `None`.
- `Stage` and `BandName` continue to round-trip by name (matching v5 semantics). The implicit single-stage rule from plan 019 still applies: when `show.Stages.Count == 0`, `Stage` column is omitted via the existing `NoStageSlotRowMap` ClassMap.
- `EarlyChain`, `PreShowEvents`, `PostShowEvents` are JSON-only and NOT in CSV. Bundle import re-derives them from the active `FestivalTimingTemplate` (or `VenueTimingOptions` for traditional mode) at the time of import.
- Per-RO scalars (`ModeOverride`, `AnchorEventOverride`, `*Override` times, `VenueOptions`, `FestivalTemplate`) are JSON-only. Bundle export does NOT include them; bundle import resets them to defaults.
- Round-trip MUST be byte-stable. Adding a column MUST update writer, reader, and `ExportServiceTests` together.

## File-by-file scope

### Models (`src/FestivalRider/Models`)

- `TimingEventType.cs` — `MACRO_CASE` enum with 13 values listed under Architecture rules.
- `ScheduleMode.cs` — enum `Traditional`, `Festival`.
- `StageLinkGroup.cs` — `Guid Id`, `List<int> StageIds`, `StageLinkConstraint Constraint`.
- `StageLinkConstraint.cs` — enum `All`, `OnStageOnly`.
- `TimingChainEntry.cs` — `TimingEventType EventType`, `string? CustomDisplayName`, `int DefaultDurationMinutes`, `bool IsOptional`.
- `FestivalTimingTemplate.cs` — `List<TimingChainEntry> EarlyChain`, `List<TimingChainEntry> PreShowEntries` (reverse chronological from anchor), `List<TimingChainEntry> PostShowEntries` (chronological from anchor), `int DefaultSetLengthMinutes`.
- `VenueTimingOptions.cs` — boolean inclusion toggles (`IncludeGetIn`, `IncludeStageLoadIn`, `IncludeSoundcheck`, `IncludeChangeover`, `IncludePreShowLinecheck`, `IncludeLoadOutStaging`, `IncludeLoadOutVenue`) plus default durations (`DefaultGetInMinutes`, `DefaultStageLoadInMinutes`, `DefaultSetupOnStageMinutes`, `DefaultSoundcheckMinutes`, `DefaultPreShowLinecheckMinutes`, `DefaultSetLengthMinutes`, `DefaultChangeoverMinutes`, `DefaultLoadOutStagingMinutes`, `DefaultLoadOutVenueMinutes`). `LOAD_IN_VENUE` is festival-only.
- `SlotTimingEvent.cs` — `TimingEventType EventType`, `DateTime? StartTime`, `int? DurationMinutes`, `bool IsPinned`. For CHANGEOVER/PRESHOW_LINECHECK entries inside a slot's `PreShowEvents`, `DurationMinutes` MUST stay null per the single-source-of-truth invariant.
- `RunningOrderSlot.cs` — replaces the v5 `record`. Properties in declaration order: `Guid Id`, `Guid BandId`, `int StageId`, `DateTime? OnStageTime`, `bool IsOnStagePinned`, `int? SetLengthMinutes`, `int? ChangeoverMinutes`, `int? PreShowLinecheckMinutes`, `int SoundcheckOrderIndex`, `List<SlotTimingEvent> EarlyChain`, `List<SlotTimingEvent> PreShowEvents`, `List<SlotTimingEvent> PostShowEvents`, `DateTime? BackstageTime`, `bool IsBackstageTimePinned`, `int? BackstageLeadMinutes`, `DateTime? BackstageCurfewTime`, `bool IsBackstageCurfewPinned`, `BandScheduleFlags Flags`, `UserOverrideFlags OverrideFlags`, `string? Notes`.
- `RunningOrder.cs` — adds (all nullable) `ScheduleMode? ModeOverride`, `TimingEventType? AnchorEventOverride`, `DateTime? TechnicalGetInTimeOverride`, `DateTime? DoorsOpeningTimeOverride`, `DateTime? FirstShowTimeOverride`, `DateTime? SoundCurfewTimeOverride`, `DateTime? BackstageCurfewTimeOverride`, `int? BreakTimeMinutesOverride`, `int? SoundcheckGapMinutesOverride`, `VenueTimingOptions? VenueOptions`, `FestivalTimingTemplate? FestivalTemplate`. Existing `Slots` continues.
- `ShowData.cs` — adds `ScheduleMode DefaultScheduleMode`, `TimingEventType DefaultAnchorEvent`, `DateTime? TechnicalGetInTime`, `DateTime? DoorsOpeningTime`, `DateTime? FirstShowTime`, `DateTime? SoundCurfewTime`, `DateTime? BackstageCurfewTime`, `int BreakTimeMinutes`, `int SoundcheckGapMinutes`, `List<StageLinkGroup> StageLinkGroups`. None of these exist in v5; all are net-new.
- `BandScheduleFlags.cs` — `[Flags]` enum: `None = 0`, `HasPersonalBackstageCurfew = 1`.
- `UserOverrideFlags.cs` — `[Flags]` enum: `None = 0`, `AllowSoundcheckOverlap = 1`, `AllowOnStageOverlap = 2`.
- `ScheduleWarning.cs` — `ScheduleWarningType Type`, `string Message`, `Guid? SlotId`, `Guid? RelatedSlotId`. Slot identity is by `RunningOrderSlot.Id`, not list index.
- `ScheduleWarningType.cs` — enum (12 values, `CateringOutsideHours` removed): `BreakTimeViolation`, `SoundcheckBlockOverlap`, `OnStageOverlap`, `BackwardLockConflict`, `BarrierConflict`, `CurfewViolation`, `SoundcheckShrunk`, `SoundcheckOrderOverlap`, `UserOverrideOverlap`, `EarlySoundcheckAfterOnStage`, `ConstraintViolation`, `FirstShowTimeMissing`.
- `ScheduleResult.cs` — `bool Success`, `List<ScheduleWarning> Warnings`. Schedule data is communicated via in-place mutation of the passed `RunningOrder`.
- `BandPlacement.cs` — `Guid BandId`, `int StageId`, `int? InsertAtIndex`, `DateTime? PinnedOnStageTime`.
- `SlotEditRequest.cs` — `Guid SlotId`, `TimingEventType EventType`, `DateTime? NewStartTime`, `bool IsPinnedChange`. Identifies slots by stable `Guid`, not list index.
- `AppState.cs` — bump default `SchemaVersion` to 6. Constructor relies on `ShowData` property defaults; no constructor-side seeding of new scalars beyond what plan 019 already does.

### Services (`src/FestivalRider/Services`)

- `IRunningOrderScheduler.cs` — methods: `ScheduleResult Recalculate(RunningOrder, ShowData)`, `ScheduleResult AddSlot(RunningOrder, BandPlacement, ShowData)`, `ScheduleResult RemoveSlot(RunningOrder, Guid slotId, ShowData)`, `ScheduleResult MoveSlot(RunningOrder, Guid slotId, int newIndex, ShowData)`, `ScheduleResult SetSoundcheckOrder(RunningOrder, Guid slotId, int newSoundcheckIndex, ShowData)`, `List<ScheduleWarning> Validate(RunningOrder, ShowData)`.
- `RunningOrderScheduler.cs` — injects `ILogger<RunningOrderScheduler>`. Private methods `ComputeTraditionalTimeline` and `ComputeFestivalTimeline`; shared helpers for overlap checks and constraint validation. All inputs resolve through the effective-value rule before computation.
- `BandService.cs` — `UpdateShow` extended per the architecture rule. `DeleteBand` cleanup unchanged but now operates on the new slot shape.
- `Program.cs` — register `IRunningOrderScheduler` as `Scoped`; register `V5ToV6Migrator` and `V5ToV6BundleMigrator`. All earlier migrator registrations stay.
- `IExportService.cs` / `ExportService.cs` — extend `SlotRow`, `NoStageSlotRowMap`, and the writer/reader to the new column list; add `Flags`/`OverrideFlags` parsing helpers; add `DateTime`-from-`HH:mm`+`DayOffset` reconstruction using `BaseDate`.

### Pages (`src/FestivalRider/Pages`)

- `RunningOrderV2.razor` — full rewrite. Orchestrator. Top toolbar (mode selector, doors, first show, technical get-in, sound curfew, backstage curfew, break time, soundcheck gap), hosts `ScheduleGantt`, `ScheduleBandPanel`, `TemplateEditor`, `VenueOptionsEditor`. Subscribes to `BandService.OnChange` and `Localization.OnLocaleChanged`. Renders warning toasts via `IToastService`. Keeps `@page "/running-order"`.

### Components (`src/FestivalRider/Components`)

- `ScheduleGantt.razor` — horizontal timeline. Color-coded bars per `TimingEventType`. Cross-day awareness with `+Nd` badges. Click-to-select, drag-to-move respecting pins (no drag-to-resize). Day offset recalculates from `BaseDate` on midnight crossing. Emits `EventCallback<SlotEditRequest>`. NEVER injects services.
- `ScheduleBandPanel.razor` — side panel. Three sections: Early Chain, Pre-Show Chain, Post-Show Chain. Each lists Event | Time | Duration | Pinned. Read-only `BackstageTime` row. `Flags` checkboxes. Per-band `BackstageCurfewTime` input. NEVER injects services.
- `TemplateEditor.razor` — modal for editing `FestivalTimingTemplate`. Drag-to-reorder entries, set default durations, toggle optional, rename display labels. Preset buttons "Festival Main Stage", "Festival Tent", "Traditional Venue".
- `VenueOptionsEditor.razor` — panel for traditional mode: include/exclude toggles per event type, default duration inputs.
- `RunningOrderSlotRow.razor` — RETIRED in this plan. Replaced by `ScheduleGantt` + `ScheduleBandPanel`. Delete the file in task 6.

### Migrators (`src/FestivalRider/Migrators`)

- `V5ToV6Migrator.cs` — converts each legacy `RunningOrderSlot` (BandId, StageId, TimeOnly StartTime, SetLengthMinutes, ChangeoverMinutes, Notes) to the new class. For each slot mints a fresh `Guid Id`. Reconstructs `OnStageTime` as `show.DateOfOpening.AddDays(ro.ShowDayNumber - 1).ToDateTime(slot.StartTime)` and sets `IsOnStagePinned = true`. Maps `SetLengthMinutes` and `ChangeoverMinutes` directly. Sets `PreShowLinecheckMinutes = null` (defaults flow from `VenueTimingOptions.DefaultPreShowLinecheckMinutes`). Sets `SoundcheckOrderIndex = (count - 1) - playingIndex` (reverse playing order). For each `RunningOrder`, creates a `VenueOptions` populated with sane defaults, sets `ModeOverride = null` so the show-level default applies, and stamps `ShowData.DefaultScheduleMode = Traditional`, `ShowData.DefaultAnchorEvent = ON_STAGE`, `ShowData.BreakTimeMinutes = 120`, `ShowData.SoundcheckGapMinutes = 0`. Stamp `schemaVersion = 6`.
- Reuses `JsonNode.DeepClone()` per the plan-019 reparenting rule.

### BundleMigrators (`src/FestivalRider/BundleMigrators`)

- `V5ToV6BundleMigrator.cs` — operates on raw running-order CSV strings (no `Models` references). For each running-order entry, parses v5 columns (`ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes`) and rewrites them to the v6 column list with the following defaults: `Id` minted fresh per row, `OnStageTime = StartTime`, `OnStageDayOffset = 0`, `IsOnStagePinned = true`, `PreShowLinecheckMinutes` empty, `SoundcheckOrderIndex` = reverse playing order (computed from row count and row index), `BackstageTime`/`BackstageDayOffset`/`IsBackstageTimePinned`/`BackstageLeadMinutes` empty, `BackstageCurfewTime`/`BackstageCurfewDayOffset`/`IsBackstageCurfewPinned` empty, `Flags` empty, `OverrideFlags` empty. Manifest format and per-RO/show entries are unchanged (RO scalars and templates are JSON-only and not in v6 bundles). Stamp manifest `schemaVersion = 6`.

### Localization (`src/FestivalRider/wwwroot/i18n`, `LocalizationKeys.cs`)

New keys to add to `en.json` and `fr-fr.json`, mirrored as constants in `LocalizationKeys.cs` (1:1 parity enforced by `LocalizationCatalogTests`):

- `enum.TimingEventType.{ValueName}` — 13 keys, one per enum value.
- `enum.ScheduleMode.{ValueName}` — 2 keys.
- `enum.ScheduleWarningType.{ValueName}` — 12 keys.
- `page.runningOrder.toolbar.modeLabel`, `.doorsLabel`, `.firstShowLabel`, `.technicalGetInLabel`, `.soundCurfewLabel`, `.backstageCurfewLabel`, `.breakLabel`, `.gapLabel`.
- `page.runningOrder.gantt.title`, `.dayBadge`, `.selectSlotHint`, `.allowOverlapBtn`.
- `page.runningOrder.panel.earlyChainHeading`, `.preShowHeading`, `.postShowHeading`, `.backstageRow`, `.flag.hasPersonalBackstageCurfew`.
- `page.runningOrder.template.title`, `.addEntryBtn`, `.removeEntryBtn`, `.optionalLabel`, `.customNameLabel`, `.preset.festivalMainStage`, `.preset.festivalTent`, `.preset.traditionalVenue`.
- `page.runningOrder.venueOptions.title`, `.includeGetIn`, `.includeStageLoadIn`, `.includeSoundcheck`, `.includeChangeover`, `.includePreShowLinecheck`, `.includeLoadOutStaging`, `.includeLoadOutVenue` (one per toggle), plus `.default.{durationKey}` per default duration field.
- `toast.schedule.warning` — single template taking `{0}` (warning title) and `{1}` (band/slot context); the title comes from `enum.ScheduleWarningType.*`.

### Tests (`tests/FestivalRider.Tests`)

- `Services/RunningOrderSchedulerTests.cs` — new. Traditional mode: forward cascade, backward soundcheck packing, break-time enforcement, on-stage overlap detection, pin-vs-cascade barrier conflicts, `FirstShowTimeMissing` fallback, soundcheck gap, soundcheck order override. Festival mode: template-based chain growth from anchor, multiple stages independent, early-chain validation, anchor override resolution, `EarlySoundcheckAfterOnStage`, `UserOverrideOverlap` downgrade.
- `Services/BandServiceTests.cs` — extend with `UpdateShow` test asserting all new scalars copy across and nested `Bands`/`RunningOrders` references stay intact.
- `Services/ExportServiceTests.cs` — new column round-trip assertion (byte-stable), implicit-stage column omission unchanged, cross-day `OnStageTime`+`OnStageDayOffset` round-trip, `Flags`/`OverrideFlags` round-trip, empty-time + zero-offset = null.
- `Migrators/V5ToV6MigratorTests.cs` — new. Reverse-soundcheck-order computation, fresh `Guid` minting per slot, `OnStageTime` reconstruction from `BaseDate + StartTime`, `IsOnStagePinned = true`, default `VenueOptions` populated, schema bump.
- `BundleMigrators/V5ToV6BundleMigratorTests.cs` — new. CSV column rewrite, defaults applied, fresh `Guid` minting, manifest schema bump, byte-stability of unrelated entries.
- `LocalizationCatalogTests.cs` — already enforces parity; add explicit assertions covering the new enum-key prefixes (`enum.TimingEventType.*` count = 13, `enum.ScheduleMode.*` count = 2, `enum.ScheduleWarningType.*` count = 12).
- `TestDataFactory.cs` — extend `FullShow()` to seed sane scheduler defaults; add `TraditionalRunningOrder()` and `FestivalRunningOrder()` factories for scheduler tests.

## Task order

Each step MUST leave the app compiling. Tests targeting modified surfaces MUST stay green at the end of every step that touches them.

1. **Models + ExportService minimal compile-fix** — Add all new model files. Convert `RunningOrderSlot` from record to class (add `Guid Id`). Update `RunningOrder`, `ShowData`, `AppState` (schema bump). Update `ExportService` (new column list, full writer + reader), `BundleService`, `BandService.UpdateShow`, `RunningOrderV2.razor` (temporary scaffolding so the page compiles), `RunningOrderSlotRow.razor` (temporary patch — final delete in task 6), `StagePrintStrategy`, `RolePrintStrategy` (use `slot.OnStageTime.TimeOfDay` for now). All existing tests updated. App compiles, tests pass except deliberately disabled scheduler-flavor tests.
2. **`IRunningOrderScheduler` + Traditional algorithm** — Define interface, implement Traditional mode end-to-end. Register `Scoped` in `Program.cs`. Add `RunningOrderSchedulerTests` for Traditional flows. App compiles, tests pass.
3. **`V5ToV6Migrator` + `V5ToV6BundleMigrator`** — Implement both. Register in `Program.cs`. Add `V5ToV6MigratorTests` and `V5ToV6BundleMigratorTests`. App compiles, tests pass.
4. **Festival mode in `RunningOrderScheduler`** — Implement `ComputeFestivalTimeline`, anchor/template growth, early-chain validation. Add Festival flows to `RunningOrderSchedulerTests`. App compiles, tests pass.
5. **Localization** — Add all new keys to `en.json`, `fr-fr.json`, and `LocalizationKeys.cs`. `LocalizationCatalogTests` green.
6. **UI components** — Implement `ScheduleGantt`, `ScheduleBandPanel`, `TemplateEditor`, `VenueOptionsEditor`. Rewrite `RunningOrderV2.razor` to host them. Delete `RunningOrderSlotRow.razor`. App compiles, runnable.
7. **Print strategy update** — Replace `slot.OnStageTime.TimeOfDay` quick-fix with cross-day-aware rendering (`+Nd` suffix) in `StagePrintStrategy` and `RolePrintStrategy`. App compiles, tests pass.
8. **Preset templates + final polish** — Add the three preset buttons in `TemplateEditor`. Wire `UserOverrideFlags` "Allow overlap" buttons in `ScheduleGantt`/`ScheduleBandPanel`. App compiles, runnable.
9. **Integration sweep** — Create a show, add bands, switch modes, exercise both timelines, export+import bundle, verify migrated v5 data loads. Commit.

## Implementation cadence

- **Wave 1 — Models + CSV byte stability + Traditional scheduler** (tasks 1–2). Demoable: traditional running order auto-computes soundcheck and on-stage times with warnings; CSV round-trips byte-stably.
- **Wave 2 — Migration** (task 3). Demoable: load v5 state, save, re-load as v6; export+import a v5 bundle into a v6 app.
- **Wave 3 — Festival scheduler + localization** (tasks 4–5). Demoable: switch a running order to festival mode via JSON edit, see chains computed.
- **Wave 4 — UI + print + presets** (tasks 6–8). Demoable: full Gantt + side panel + template editor with presets; print strategies render cross-day timing correctly.
- **Wave 5 — Integration verification** (task 9). Demoable: end-to-end smoke pass.

## Out of scope

- Stage linking UI and enforcement (`StageLinkGroups` schema only).
- Auto-packing soundchecks into festival-mode gaps.
- Drag-to-resize duration on the Gantt.
- Live cross-tab sync of running-order edits.
- PDF export of the Gantt chart.
- Catering data model and meal-window warnings (`TimeSlot`, `CateringOutsideHours`).
- Per-RO scalar and template round-trip in bundle CSV (JSON-only in v6).

## Risks & migrations

- **Legacy `RunningOrderSlot` record → class** — v5 records are immutable; v6 instances are mutable. `V5ToV6Migrator` must build full new-shape instances with sane defaults; tests cover empty slots, missing notes, pinned/unpinned semantics.
- **CSV column expansion** — v6 column list is locked here; this amends the AGENTS.md root rule. Writer, reader, and `ExportServiceTests` ship together. Cross-day data round-trips via `*DayOffset` integer columns.
- **`DateTime` vs `TimeOnly` in print strategies** — convert at the boundary (`OnStageTime.TimeOfDay` for same-day rendering, `+Nd` badge for cross-day). Task 7 hardens this.
- **Per-RO and template loss on bundle round-trip** — v6 bundle CSV does not persist per-RO scalars or templates; bundle import resets them. Documented in Decisions and in toast messaging at import time. Successor plan may extend.
- **`StageLinkGroups` orphan IDs** — deleting a stage does not clean up references. Scheduler skips non-existent stages; v6 ignores the structure entirely.
- **Default-mode silent flip on UpdateShow** — `BandService.UpdateShow` MUST copy every new show-level scalar; failing to do so silently wipes scheduler config on edit. Architecture rule + `BandServiceTests` regression test cover this.
- **Single-source-of-truth invariant drift** — if a future contributor stores `DurationMinutes` on a `CHANGEOVER` or `PRESHOW_LINECHECK` `SlotTimingEvent`, the value silently drifts from the slot scalar. Architecture rule is the only guard; consider an assertion in `RunningOrderSchedulerTests`.
