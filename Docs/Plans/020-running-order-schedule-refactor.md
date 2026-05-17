# 020 — Running order schedule refactor (revision V3)

> WIP successor draft of `Docs/Plans/020-running-order-schedule-refactor.md`. Plan 020 is `Draft`, so once accepted these revisions fold back in place; no new plan number is minted.
>
> This revision strips inline C# / JSON snippets per `Docs/Plans/AGENTS.md` ("Plans MUST stay dense reference prose. NEVER embed implementation snippets that belong in code") and re-applies the structural fixes from `Docs/WIP/ro-refactor-V2.md` while **preserving the catering and `TimeSlot` scope** that V2 incorrectly dropped.

## Status

Draft

## Context

Successor to [019-show-scoped-bands.md](../Plans/019-show-scoped-bands.md). Plan 019 made shows self-contained (bands, running orders, stages) but left the running order as a flat table of `StartTime` / `SetLength` / `Changeover` records with no automatic timing logic. Real-world usage requires two distinct workflows: a traditional venue timeline (soundchecks packed before doors, sequential on-stage times) and a festival timeline (interleaved stages, early soundchecks, independent stage use, per-band event chains). This plan replaces the immutable `RunningOrderSlot` record with a rich event-chain model, introduces a dual-mode scheduler, anchors per-band catering to the day's meal windows, and adds an interactive Gantt + side-panel UI.

## Decisions (locked)

- **Dual schedule mode** — `RunningOrder.ModeOverride` resolved against `ShowData.DefaultScheduleMode`. Two scheduler algorithms share one data model; mode-specific config lives in nullable child objects (`VenueTimingOptions`, `FestivalTimingTemplate`). No subclassing — it would break CSV round-trip, bundle migration, and `System.Text.Json` polymorphism in Blazor WASM.
- **Event chain template** — Festival mode uses per-RO `FestivalTimingTemplate` with reorderable `TimingChainEntry` lists (early, pre-show reverse-chronological, post-show chronological). Multiple entries of the same type are allowed.
- **Anchor event** — Festival anchor resolves `ro.AnchorEventOverride ?? show.DefaultAnchorEvent ?? ON_STAGE`. Traditional always anchors on `ON_STAGE`.
- **Cross-day `DateTime`** — All in-memory timing fields are `DateTime`. `BaseDate = show.DateOfOpening.AddDays(ro.ShowDayNumber - 1)`. UI renders post-midnight times with `+Nd` badges. CSV pairs `HH:mm` with an integer `*DayOffset` column so cross-day data round-trips losslessly.
- **Seven net-new show-level schedule scalars** — `ShowData` gains `VenueOpenTime`, `VenueCloseTime`, `TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, `SoundCurfewTime`, `BackstageCurfewTime` (all `DateTime?`). **None exist in v5.** Each is mirrored as `RunningOrder.*Override`. `EffectiveFirstShow` fallback is `BaseDate.AddHours(20)` with a `FirstShowTimeMissing` warning. `VenueOpenTime` / `VenueCloseTime` bound the window during which any per-band `LOAD_IN_VENUE` event may occur; falling outside emits `VenueClosed`.
- **`LOAD_IN_VENUE` and `BACKSTAGE_DROP` are per-band events under venue-wide toggles** — both live in `Slot.PreShowEvents` like `LOAD_IN_STAGE` and `SOUNDCHECK`. They follow the same toggle pattern as the rest of the venue chain: `VenueTimingOptions.IncludeLoadInVenue` and `IncludeBackstageDrop` are venue-wide policy ("does this venue offer a staging area?", "does this venue have backstage rooms?"). When a toggle is on, every band's slot auto-seeds the event with a computed `StartTime`; when off, no slot gets one (and the user may still add it ad-hoc via the side panel). `LOAD_IN_VENUE` is the time a band's crew unloads into the venue staging area (storage-space conflicts are user-managed and out of scope). `BACKSTAGE_DROP` is the time a band takes possession of their backstage after dropping their items. Two bands have independent `LOAD_IN_VENUE` / `BACKSTAGE_DROP` events that may overlap or not; there is no venue-wide single instance.
- **Effective-value resolution rule** — for every show-level scalar X with an RO-level override, the effective value is `ro.XOverride ?? show.X`. Scheduler MUST consult only effective values.
- **Custom event display names** — `TimingChainEntry.CustomDisplayName` is UI-only; scheduler logic always uses the enum value.
- **On-stage sequence** — `CHANGEOVER` ends, then `PRESHOW_LINECHECK`, then `ON_STAGE`. `BackstageTime` is computed by default as `earliest(CHANGEOVER.StartTime, PRESHOW_LINECHECK.StartTime, OnStageTime) - BackstageLeadMinutes`; missing pre-show events drop out of the min, leaving `OnStageTime` as the floor. Pin-able via `IsBackstageTimePinned`; when pinned, the stored value wins. `CHANGEOVER` and `PRESHOW_LINECHECK` durations come from their `SlotTimingEvent.DurationMinutes` (with venue/template default fallback).
- **Per-band backstage curfew** — `Slot.BackstageCurfewTime` is consulted only when `Slot.Flags & HasPersonalBackstageCurfew != 0`. Otherwise slot inherits `EffectiveBackstageCurfew` from the RO. `IsBackstageCurfewPinned` captures per-slot pinning separately.
- **Soundcheck packing (traditional)** — Pack backward from `min(EffectiveDoors, EffectiveFirstShow) - EffectiveBreakTime` in `SoundcheckOrderIndex` order (default = reverse of playing order). Same-stage consecutive soundchecks separated by `EffectiveSoundcheckGap`. Festival mode packs per-band chains from anchor instead, that include soundchecks or not.
- **Early soundcheck** — Festival headliners may carry an `EarlyChain` (morning soundcheck/load-in/load-out to staging area, before re-entering in schedule later). Scheduler validates `EarlyChain.End <= OnStageTime` on the same stage and emits `EarlySoundcheckAfterOnStage` on violation. Early soundcheck is signaled by `Slot.EarlyChain.Count > 0`; no-soundcheck by absence of a `SOUNDCHECK` entry in `PreShowEvents`. These are NOT `BandScheduleFlags` values.
- **Catering windows** — `ShowData.BreakfastHours`, `LunchHours`, `DinnerHours` are nullable `TimeSlot`. Each RO may override via `*HoursOverride`. `RunningOrderSlot.CateringSlot` is a per-band nullable `TimeSlot`; scheduler emits `CateringOutsideHours` when the slot falls outside every active meal window for that day.
- **`TimeSlot` anchoring** — `Start` and `End` are absolute `DateTime` values referenced to `BaseDate`. `End == null` means point-in-time. `BandService.UpdateShow` and `BandService.UpdateRunningOrder` MUST programmatically shift every affected `TimeSlot` (show-level meal hours, RO-level meal-hour overrides, every `Slot.CateringSlot`) by the delta when `ShowData.DateOfOpening` or `RunningOrder.ShowDayNumber` changes, preserving time-of-day. UI displays time-of-day with implicit date.
- **Stage linking schema-only** — `ShowData.StageLinkGroups` ships schema and `StageLinkConstraint` (`All` / `OnStageOnly`); v6 scheduler MUST ignore the structure. UI and enforcement deferred to a successor plan.
- **Orphaned `StageLinkGroup.StageIds`** — `BandService.DeleteStage` MUST remove the deleted stage id from every `ShowData.StageLinkGroups[*].StageIds` and prune any group whose `StageIds` becomes empty (or has fewer than 2 entries, since a one-stage link is meaningless). Same cleanup MUST run during `V5ToV6Migrator` for any pre-existing stale ids.
- **`RunningOrderSlot.Id`** — slots gain `Guid Id` defaulted to `Guid.NewGuid()` so the Gantt, side panel, warnings, and edit requests address slots by stable identity instead of list index. Migrators mint fresh ids.
- **`RunningOrderSlot` is mutable `class { get; set; }`** — overrides root `AGENTS.md` mutability example. Plan-wins meta-rule applies.
- **Per-RO scalars and templates are JSON-only in bundles** — `ModeOverride`, `AnchorEventOverride`, the five time overrides, the three meal-hour overrides, `BreakTimeMinutesOverride`, `SoundcheckGapMinutesOverride`, `VenueOptions`, and `FestivalTemplate` persist only in `AppState` JSON. v6 bundle CSV does NOT round-trip them; import resets them to defaults and surfaces `toast.bundle.import.roConfigReset`.
- **Schema bump** — `AppState.SchemaVersion` 5 → 6. `V5ToV6Migrator` (state) and `V5ToV6BundleMigrator` (bundle) ship together.

## Open questions

None.

## Architecture rules

### Naming, mutability, registration

- `TimingEventType` MUST use `MACRO_CASE` with exactly 13 values: `GET_IN`, `LOAD_IN_VENUE`, `LOAD_IN_STAGE`, `BACKSTAGE_DROP`, `CATERING`, `SETUP_ON_STAGE`, `SOUNDCHECK`, `CHANGEOVER`, `PRESHOW_LINECHECK`, `ON_STAGE`, `LOAD_OUT_STAGING`, `LOAD_OUT_VENUE`, `BACKSTAGE_WAIT`. Rationale: visual signal for wire-format tokens (CSV columns, JSON properties, `enum.TimingEventType.{value}` localization keys); distinguishes them from the codebase's other PascalCase enums.
- `FREETIME` MUST NOT exist as an enum value; empty Gantt gaps are UI-only.
- `RunningOrderSlot`, `SlotTimingEvent`, `TimingChainEntry`, `FestivalTimingTemplate`, `VenueTimingOptions`, `StageLinkGroup`, `TimeSlot`, `ScheduleWarning`, `ScheduleResult`, `BandPlacement`, `SlotEditRequest` MUST be mutable `class { get; set; }`.
- `BandScheduleFlags` MUST contain only `None = 0` and `HasPersonalBackstageCurfew = 1`. `Headliner`, `EarlySoundcheck`, `NoSoundcheck` MUST NOT exist as flags.
- `UserOverrideFlags` MUST contain `None = 0`, `AllowSoundcheckOverlap = 1`, `AllowOnStageOverlap = 2`.
- `Program.cs` MUST register `IRunningOrderScheduler` as `Scoped`, `V5ToV6Migrator` as `IStateMigrator`, `V5ToV6BundleMigrator` as `IBundleMigrator`. All earlier registrations stay.

### Effective-value resolution

- The effective value of any show-level scalar X with an RO override is `ro.XOverride ?? show.X`. Applies to: `ScheduleMode`, `AnchorEvent`, `VenueOpenTime`, `VenueCloseTime`, `TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, `SoundCurfewTime`, `BackstageCurfewTime`, `BreakfastHours`, `LunchHours`, `DinnerHours`, `BreakTimeMinutes`, `SoundcheckGapMinutes`.
- Festival anchor adds a final `?? ON_STAGE` fallback.
- First-show fallback: when `EffectiveFirstShow` is null, scheduler MUST use `BaseDate.AddHours(20)` and emit `FirstShowTimeMissing` exactly once per `Recalculate` call.

### Scheduler contract

- `IRunningOrderScheduler` MUST be the sole computer of timing chains. `BandService` MUST NOT contain scheduler logic.
- **Initial event placement** — the scheduler seeds a `SlotTimingEvent` with a computed `StartTime` and `IsPinned = false` in two situations: (a) when a slot is first added (or a `VenueTimingOptions.Include*` toggle flips `false` → `true`), for every event whose toggle is on (`GET_IN`, `LOAD_IN_VENUE`, `LOAD_IN_STAGE`, `BACKSTAGE_DROP`, `SETUP_ON_STAGE`, `SOUNDCHECK`, `PRESHOW_LINECHECK`); (b) when the user adds an ad-hoc event to a slot via the side panel (`CATERING`, `BACKSTAGE_WAIT`, or any other type). Subsequent recalculations honour user pins / moves; unpinned events stay re-derived. Per-event placement rules: `GET_IN`, `LOAD_IN_STAGE`, `SETUP_ON_STAGE`, `SOUNDCHECK`, `PRESHOW_LINECHECK` follow the existing backward cascade from `OnStageTime`. `LOAD_IN_VENUE` → `EffectiveVenueOpenTime` if set, else `EffectiveTechnicalGetInTime`, else `BaseDate.AddHours(8)`. `BACKSTAGE_DROP` → `max(EffectiveVenueOpenTime, LOAD_IN_VENUE.End)` when `LOAD_IN_VENUE` is present on the slot, else `SoundcheckStart - DefaultBackstageDropMinutes`, else `OnStageTime - 6h`.
- **Venue-open window validation** — for every `SlotTimingEvent` whose `EventType == LOAD_IN_VENUE`, scheduler MUST emit `VenueClosed` if `StartTime < EffectiveVenueOpenTime` or `StartTime + DurationMinutes > EffectiveVenueCloseTime`. When either bound is null the corresponding check is skipped.
- Scheduler mutates the passed `RunningOrder` graph in place. Pages MUST then call `IBandService.UpdateRunningOrder(ro)` to commit. The "BandService is the sole `AppState` mutator" rule remains: scheduler mutates a graph node held by the page; BandService commits the parent collection.
- Method names: `Recalculate`, `AddSlot`, `RemoveSlot`, `MoveSlot`, `SetSoundcheckOrder`, `Validate`. NEVER `AddBand`/`RemoveBand`/`MoveBand` — those would collide with `IBandService.AddBand`/`DeleteBand`.
- All scheduler methods identify slots by `Guid slotId`, never by list index.
- `Recalculate` MUST be idempotent: two consecutive calls with no intermediate user mutation MUST yield identical schedule and warning set.
- Scheduler MUST emit one `ScheduleWarning` per detected constraint violation. Setting `Slot.OverrideFlags` downgrades overlap warnings to informational `UserOverrideOverlap`.
- Scheduler MUST ignore `ShowData.StageLinkGroups` in v6. Only same-stage overlap checks ship.

### Duration resolution

- Every `SlotTimingEvent.DurationMinutes` is the canonical duration for that event row. `null` falls back to `VenueTimingOptions.Default*` (traditional) or the matching `FestivalTimingTemplate` entry default (festival). There is no per-slot duration scalar; `CHANGEOVER` and `PRESHOW_LINECHECK` durations live exclusively on their event rows.
- `SlotTimingEvent.IsPinned` refers to that event's start time only. Duration is either set on the event row (non-null `DurationMinutes`) or read from template/options default. Drag-to-resize is out of scope for v6.

### State and bundle persistence

- `BandService.AddRunningOrder` MUST set `order.ShowId = ActiveShowId` before adding (already enforced in v5).
- `BandService.UpdateRunningOrder` MUST replace the entire `RunningOrder` object including `Slots`, `VenueOptions`, and `FestivalTemplate` references in the active show's `RunningOrders`.
- `BandService.UpdateShow` MUST copy every new `ShowData` scalar — `DefaultScheduleMode`, `DefaultAnchorEvent`, `VenueOpenTime`, `VenueCloseTime`, `TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, `SoundCurfewTime`, `BackstageCurfewTime`, `BreakfastHours`, `LunchHours`, `DinnerHours`, `BreakTimeMinutes`, `SoundcheckGapMinutes`, `StageLinkGroups` — into the existing show record. Preserve nested `Bands` and `RunningOrders` references unchanged (plan 019 reparenting rule). When `DateOfOpening` changes, MUST shift every show-level meal `TimeSlot`, every RO meal-hour override `TimeSlot`, and every `Slot.CateringSlot` across all `RunningOrders` by `(newDate - oldDate)` days, preserving time-of-day.
- `BandService.UpdateRunningOrder` MUST shift every meal-hour override `TimeSlot` and every `Slot.CateringSlot` by `(newShowDayNumber - oldShowDayNumber)` days when `ShowDayNumber` changes.
- `BandService.DeleteStage` MUST remove the deleted `stageId` from every `ShowData.StageLinkGroups[*].StageIds` and drop any group whose `StageIds.Count < 2` after pruning.
- All persisted numeric/date conversions MUST use `CultureInfo.InvariantCulture`.

### Localization

- Every new localization key MUST ship in `en.json` and `fr-fr.json` in the same commit, mirrored as a constant in `LocalizationKeys.cs`. `LocalizationCatalogTests` parity stays green.
- `enum.TimingEventType.{ValueName}`, `enum.ScheduleMode.{ValueName}`, `enum.ScheduleWarningType.{ValueName}` keys ship alongside the corresponding enum values.

## CSV format

This section is authoritative for the v6 running-order CSV and amends the root `AGENTS.md` running-order column rule.

- Running-order CSV columns, locked in declaration order: `Id,ShowId,BandName,Stage,OnStageTime,OnStageDayOffset,IsOnStagePinned,SetLengthMinutes,SoundcheckOrderIndex,BackstageTime,BackstageDayOffset,IsBackstageTimePinned,BackstageLeadMinutes,BackstageCurfewTime,BackstageCurfewDayOffset,IsBackstageCurfewPinned,CateringStart,CateringStartDayOffset,CateringEnd,CateringEndDayOffset,Flags,OverrideFlags,Notes`. `CHANGEOVER` and `PRESHOW_LINECHECK` durations live in the JSON-only `PreShowEvents` list and are NOT in CSV; bundle import re-seeds them from the active template / venue options.
- `OnStageTime`, `BackstageTime`, `BackstageCurfewTime`, `CateringStart`, `CateringEnd` use `HH:mm` (`CultureInfo.InvariantCulture`). The paired `*DayOffset` column is an integer (`0` = same day, `1` = next day). Empty time string with offset `0` means null.
- `CateringEnd` empty with `CateringStart` non-empty encodes a point-in-time meal slot.
- `Flags` and `OverrideFlags` emit as comma-separated enum value names (e.g., `HasPersonalBackstageCurfew`). Empty means `None`.
- `BandName` kept for human readability; bundle import resolves rows to bands by case-sensitive name match against the bundle's bands.
- When `show.Stages.Count == 0`, the `Stage` column is omitted via the existing `NoStageSlotRowMap` ClassMap (plan 019).
- `Id` is a fresh `Guid` for v5-migrated slots (minted by `V5ToV6BundleMigrator`).
- `EarlyChain`, `PreShowEvents`, `PostShowEvents` are JSON-only and NOT in CSV. Bundle import re-derives them at decode time from the active template or venue options.
- Per-RO scalars and templates are JSON-only. Bundle import resets them to defaults and surfaces `toast.bundle.import.roConfigReset`.
- Round-trip MUST be byte-stable. Adding a column MUST update writer, reader, and `ExportServiceTests` together.

## File-by-file scope

### Models (`src/FestivalRider/Models`)

- `TimeSlot.cs` — `DateTime Start`, `DateTime? End` (null = point-in-time).
- `TimingEventType.cs` — `MACRO_CASE` enum, 13 values (see Architecture rules).
- `ScheduleMode.cs` — `Traditional`, `Festival`.
- `StageLinkConstraint.cs` — `All`, `OnStageOnly`.
- `StageLinkGroup.cs` — `Guid Id` (auto), `List<int> StageIds`, `StageLinkConstraint Constraint` (default `All`).
- `TimingChainEntry.cs` — `TimingEventType EventType`, `string? CustomDisplayName`, `int DefaultDurationMinutes`, `bool IsOptional`.
- `FestivalTimingTemplate.cs` — `List<TimingChainEntry> EarlyChain`, `List<TimingChainEntry> PreShowEntries` (reverse chronological from anchor; index 0 closest to anchor), `List<TimingChainEntry> PostShowEntries` (chronological), `int DefaultSetLengthMinutes`.
- `VenueTimingOptions.cs` — inclusion toggles governing which events auto-seed into every slot's `PreShowEvents`: `IncludeGetIn`, `IncludeLoadInVenue`, `IncludeStageLoadIn`, `IncludeBackstageDrop`, `IncludeSetupOnStage`, `IncludeSoundcheck`, `IncludePreShowLinecheck`. Defaults: `IncludeGetIn`, `IncludeStageLoadIn`, `IncludeSetupOnStage`, `IncludeSoundcheck`, `IncludePreShowLinecheck` default `true`; `IncludeLoadInVenue` and `IncludeBackstageDrop` default `false` (optional venue capabilities). `CHANGEOVER` is always present between consecutive same-stage bands; do NOT add a toggle. `CATERING` and `BACKSTAGE_WAIT` are ad-hoc per-band events added via the side panel and do NOT have venue-level toggles. Default duration scalars: `DefaultGetInMinutes`, `DefaultLoadInVenueMinutes`, `DefaultStageLoadInMinutes`, `DefaultBackstageDropMinutes`, `DefaultSetupOnStageMinutes`, `DefaultSoundcheckMinutes`, `DefaultPreShowLinecheckMinutes`, `DefaultBackstageLeadMinutes`, `DefaultChangeoverMinutes`, `DefaultSetLengthMinutes`. All 13 `TimingEventType` values are valid in venue mode.
- `SlotTimingEvent.cs` — `TimingEventType EventType`, `DateTime? StartTime`, `int? DurationMinutes`, `bool IsPinned`. `DurationMinutes` is canonical for every event row; `null` falls back to `VenueTimingOptions.Default*` (traditional) or the matching `FestivalTimingTemplate` entry default (festival).
- `RunningOrderSlot.cs` — mutable class. Properties in declaration order: `Guid Id`, `Guid BandId`, `int StageId`, `DateTime? OnStageTime`, `bool IsOnStagePinned`, `int? SetLengthMinutes`, `int SoundcheckOrderIndex`, `List<SlotTimingEvent> EarlyChain`, `List<SlotTimingEvent> PreShowEvents`, `List<SlotTimingEvent> PostShowEvents`, `DateTime? BackstageTime`, `bool IsBackstageTimePinned`, `int? BackstageLeadMinutes`, `DateTime? BackstageCurfewTime`, `bool IsBackstageCurfewPinned`, `TimeSlot? CateringSlot`, `BandScheduleFlags Flags`, `UserOverrideFlags OverrideFlags`, `string? Notes`. Changeover and pre-show-linecheck durations live on the corresponding `SlotTimingEvent` rows inside `PreShowEvents`, not as slot scalars.
- `RunningOrder.cs` — keeps `Guid Id`, `Guid ShowId`, `int ShowDayNumber`, `List<RunningOrderSlot> Slots`. Adds (all nullable) `ScheduleMode? ModeOverride`, `TimingEventType? AnchorEventOverride`, `DateTime? VenueOpenTimeOverride`, `DateTime? VenueCloseTimeOverride`, `DateTime? TechnicalGetInTimeOverride`, `DateTime? DoorsOpeningTimeOverride`, `DateTime? FirstShowTimeOverride`, `DateTime? SoundCurfewTimeOverride`, `DateTime? BackstageCurfewTimeOverride`, `TimeSlot? BreakfastHoursOverride`, `TimeSlot? LunchHoursOverride`, `TimeSlot? DinnerHoursOverride`, `int? BreakTimeMinutesOverride`, `int? SoundcheckGapMinutesOverride`, `VenueTimingOptions? VenueOptions`, `FestivalTimingTemplate? FestivalTemplate`.
- `ShowData.cs` — adds `ScheduleMode DefaultScheduleMode` (default `Traditional`), `TimingEventType DefaultAnchorEvent` (default `ON_STAGE`), nullable `DateTime? VenueOpenTime`, `DateTime? VenueCloseTime`, `DateTime? TechnicalGetInTime`, `DateTime? DoorsOpeningTime`, `DateTime? FirstShowTime`, `DateTime? SoundCurfewTime`, `DateTime? BackstageCurfewTime`, nullable `TimeSlot? BreakfastHours`, `TimeSlot? LunchHours`, `TimeSlot? DinnerHours`, `int BreakTimeMinutes` (default 120), `int SoundcheckGapMinutes` (default 0), `List<StageLinkGroup> StageLinkGroups`. **None of these exist in v5; all are net-new.**
- `BandScheduleFlags.cs` — `[Flags]` enum: `None = 0`, `HasPersonalBackstageCurfew = 1`.
- `UserOverrideFlags.cs` — `[Flags]` enum: `None = 0`, `AllowSoundcheckOverlap = 1`, `AllowOnStageOverlap = 2`.
- `ScheduleWarningType.cs` — 14 values: `BreakTimeViolation`, `SoundcheckBlockOverlap`, `OnStageOverlap`, `BackwardLockConflict`, `BarrierConflict`, `CateringOutsideHours`, `CurfewViolation`, `SoundcheckShrunk`, `SoundcheckOrderOverlap`, `UserOverrideOverlap`, `EarlySoundcheckAfterOnStage`, `ConstraintViolation`, `FirstShowTimeMissing`, `VenueClosed`.
- `ScheduleWarning.cs` — `ScheduleWarningType Type`, `string Message`, `Guid? SlotId`, `Guid? RelatedSlotId`.
- `ScheduleResult.cs` — `bool Success`, `List<ScheduleWarning> Warnings`. Schedule itself is communicated via in-place mutation of the passed `RunningOrder`.
- `BandPlacement.cs` — `Guid BandId`, `int StageId`, `int? InsertAtIndex`, `DateTime? PinnedOnStageTime`. Lives under `Models/` (not `Services/`) per the layering rule "Models: data only".
- `SlotEditRequest.cs` — `Guid SlotId`, `TimingEventType EventType`, `DateTime? NewStartTime`, `bool ToggledPin`.
- `AppState.cs` — bumps default `SchemaVersion` to `6`. Constructor unchanged; defaults flow from new `ShowData` property defaults.

### Services (`src/FestivalRider/Services`)

- `IRunningOrderScheduler.cs` — `ScheduleResult Recalculate(RunningOrder, ShowData)`, `ScheduleResult AddSlot(RunningOrder, BandPlacement, ShowData)`, `ScheduleResult RemoveSlot(RunningOrder, Guid slotId, ShowData)`, `ScheduleResult MoveSlot(RunningOrder, Guid slotId, int newIndex, ShowData)`, `ScheduleResult SetSoundcheckOrder(RunningOrder, Guid slotId, int newSoundcheckIndex, ShowData)`, `List<ScheduleWarning> Validate(RunningOrder, ShowData)`.
- `RunningOrderScheduler.cs` — injects `ILogger<RunningOrderScheduler>`. Private methods `ComputeTraditionalTimeline` and `ComputeFestivalTimeline` with shared helpers (effective-value resolution, overlap checks, catering-window check, curfew check). Traditional pipeline: resolve globals → forward on-stage cascade per stage with barrier-aware pinned-slot handling → backward soundcheck packing per stage from `min(EffectiveDoors, EffectiveFirstShow) - EffectiveBreakTime` in `SoundcheckOrderIndex` order, separated by `EffectiveSoundcheckGap` → derive `BackstageTime` unless pinned → catering/curfew/overlap emission. Festival pipeline: resolve globals → per-band chain growth from anchor (backward through `PreShowEvents`, forward through `PostShowEvents`) honouring per-event pins → validate `EarlyChain.End <= OnStageTime` per stage → same-stage overlap checks only → catering/curfew emission.
- `BandService.cs` — extend `UpdateShow` per the architecture rule. No scheduler logic.
- `Program.cs` — additions: `AddScoped<IRunningOrderScheduler, RunningOrderScheduler>()`; `AddScoped<IStateMigrator, V5ToV6Migrator>()`; `AddScoped<IBundleMigrator, V5ToV6BundleMigrator>()`. All earlier registrations stay.
- `IExportService.cs` / `ExportService.cs` — extend `SlotRow`, `NoStageSlotRowMap`, and writer/reader to the new column list. Helpers: `Flags`/`OverrideFlags` round-trip (comma-separated enum names), `DateTime`-from-`HH:mm`+`*DayOffset` reconstruction using `show.DateOfOpening.AddDays(ro.ShowDayNumber - 1)`, `TimeSlot` round-trip from the four catering columns.

### Pages (`src/FestivalRider/Pages`)

- `RunningOrderV2.razor` — full rewrite. Orchestrator. Toolbar exposes mode selector, anchor event, venue open / close, technical get-in, doors, first show, sound curfew, backstage curfew, break time, soundcheck gap, and the three meal-window editors. Hosts `ScheduleGantt`, `ScheduleBandPanel`, `TemplateEditor`, `VenueOptionsEditor`. Subscribes to `BandService.OnChange` and `Localization.OnLocaleChanged`. Renders `ScheduleResult.Warnings` via `IToastService`. After every mutation: call `IRunningOrderScheduler.Recalculate`, then `IBandService.UpdateRunningOrder`. Route stays `/running-order`.

### Components (`src/FestivalRider/Components`)

- `ScheduleGantt.razor` — horizontal timeline. Color-coded bars per `TimingEventType` (orange = soundcheck, red = early chain, green = on-stage, blue = post-show, yellow = other). `+Nd` badges driven by `BaseDate`. Click-to-select emits `EventCallback<Guid>` (slot id). Drag-to-move emits `EventCallback<SlotEditRequest>`. NEVER injects services.
- `ScheduleBandPanel.razor` — side panel. Sections: Early Chain (visible only if `Slot.EarlyChain.Count > 0`), Pre-Show Chain (reverse chronological with read-only derived row for `BackstageTime`), Post-Show Chain (chronological), Constraints (`BackstageCurfewTime`, `HasPersonalBackstageCurfew` checkbox, `CateringSlot` inputs), Override Flags. Each event row in the chain lists exposes a `DurationMinutes` input that binds to `SlotTimingEvent.DurationMinutes`; empty input means "use default" (placeholder displays the active venue/template default). The Pre-Show section also exposes an **"Add event"** affordance for ad-hoc types (`CATERING`, `BACKSTAGE_WAIT`, and any other `TimingEventType` the user wants to add on a single slot without flipping a venue-wide toggle); the scheduler computes the initial `StartTime` per the placement rules and the user may pin / move freely afterward.
- `TemplateEditor.razor` — modal for `FestivalTimingTemplate`. Three sortable lists (Early, Pre-Show, Post-Show). Preset buttons "Festival Main Stage", "Festival Tent", "Traditional Venue" populate sensible defaults.
- `VenueOptionsEditor.razor` — traditional-mode inclusion toggles and default-duration inputs.
- `RunningOrderSlotRow.razor` — **RETIRED**. The new UI replaces it entirely. Delete the file in the same wave that wires `ScheduleGantt` / `ScheduleBandPanel` into `RunningOrderV2.razor`.

### Migrators (`src/FestivalRider/Migrators`)

- `V5ToV6Migrator.cs` — converts every legacy slot. Mints a fresh `Guid Id`. Reconstructs `OnStageTime` as `show.DateOfOpening.AddDays(ro.ShowDayNumber - 1).ToDateTime(slot.StartTime)`; sets `IsOnStagePinned = true`. Maps `SetLengthMinutes` directly. Migrates the v5 `ChangeoverMinutes` scalar into a freshly-seeded `CHANGEOVER` `SlotTimingEvent` inside `PreShowEvents` with `DurationMinutes = v5ChangeoverMinutes`; leaves the auto-seeded `PRESHOW_LINECHECK` event row's `DurationMinutes = null` (default fallback). Leaves `BackstageLeadMinutes`, `BackstageTime`, `BackstageCurfewTime`, `CateringSlot` null. Sets `SoundcheckOrderIndex = (slotCount - 1) - playingIndex` (reverse playing order). For each `RunningOrder`, creates a `VenueOptions` with default durations, leaves `FestivalTemplate` null, leaves all `*Override` fields null. For each `ShowData`, sets `DefaultScheduleMode = Traditional`, `DefaultAnchorEvent = ON_STAGE`, `BreakTimeMinutes = 120`, `SoundcheckGapMinutes = 0`; leaves the seven new `DateTime?` scalars (`VenueOpenTime`, `VenueCloseTime`, `TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, `SoundCurfewTime`, `BackstageCurfewTime`) and three meal-hour slots null. Cleans `StageLinkGroups`: drops every `StageId` not present in `ShowData.Stages`, then drops any group with fewer than 2 remaining ids. Stamps `schemaVersion = 6`. Reuses `JsonNode.DeepClone()` per plan-019 reparenting rule. Reads raw JSON for v5 slots; MUST NOT reference the old `RunningOrderSlot` record type at compile time.

### BundleMigrators (`src/FestivalRider/BundleMigrators`)

- `V5ToV6BundleMigrator.cs` — operates on raw running-order CSV strings only. MUST NOT reference `FestivalRider.Models` types (per `BundleMigrators/AGENTS.md`). Parses v5 columns (`ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes`) and rewrites to the v6 column list with defaults: mint fresh `Id` per row; `OnStageTime = StartTime` (string passthrough); `OnStageDayOffset = 0`; `IsOnStagePinned = true`; `BackstageLeadMinutes`, `BackstageTime`, `BackstageDayOffset`, `IsBackstageTimePinned`, `BackstageCurfewTime`, `BackstageCurfewDayOffset`, `IsBackstageCurfewPinned`, `CateringStart`, `CateringStartDayOffset`, `CateringEnd`, `CateringEndDayOffset`, `Flags`, `OverrideFlags` all empty; `SoundcheckOrderIndex` computed by row position (reverse playing order). The v5 `ChangeoverMinutes` column is dropped from CSV; on import, `BundleService` (or the post-decode seed step) re-seeds the `CHANGEOVER` `SlotTimingEvent` with `DurationMinutes = null` (default fallback) since the v5 value cannot survive the JSON-only event list. Stamps manifest `schemaVersion = 6`. Manifest entries `bands` and `show` are passed through unchanged.

### Localization (`src/FestivalRider/wwwroot/i18n`, `LocalizationKeys.cs`)

New keys added to both `en.json` and `fr-fr.json`, mirrored in `LocalizationKeys.cs`:

- 13 `enum.TimingEventType.{value}` keys.
- 2 `enum.ScheduleMode.{value}` keys.
- 14 `enum.ScheduleWarningType.{value}` keys (includes `VenueClosed`).
- Toolbar labels under `page.runningOrder.toolbar.*`: `modeLabel`, `anchorLabel`, `venueOpenLabel`, `venueCloseLabel`, `technicalGetInLabel`, `doorsLabel`, `firstShowLabel`, `soundCurfewLabel`, `backstageCurfewLabel`, `breakLabel`, `gapLabel`, `breakfastLabel`, `lunchLabel`, `dinnerLabel`.
- Gantt: `page.runningOrder.gantt.title`, `.dayBadge`, `.selectSlotHint`, `.allowOverlapBtn`, plus `.legend.soundcheck`, `.legend.onStage`, `.legend.early`, `.legend.post`.
- Side panel: `page.runningOrder.panel.earlyChainHeading`, `.preShowHeading`, `.postShowHeading`, `.backstageRow`, `.changeover`, `.linecheck`, `.catering`, `.backstageCurfew`, `.flag.hasPersonalBackstageCurfew`.
- Template editor: `page.runningOrder.template.title`, `.addEntryBtn`, `.removeEntryBtn`, `.optionalLabel`, `.customNameLabel`, `.preset.festivalMainStage`, `.preset.festivalTent`, `.preset.traditionalVenue`.
- Venue options: `page.runningOrder.venueOptions.title` and `.include*` + `.default*` per toggle/default, including `.includeLoadInVenue`, `.includeBackstageDrop`, `.defaultLoadInVenueMinutes`, `.defaultBackstageDropMinutes`.
- Side panel "Add event" picker: `page.runningOrder.panel.addEventBtn`, `.addEventPickerTitle`, `.addEventPlacementHint`.
- Toasts: `toast.schedule.warning` (template taking `{0}` warning title and `{1}` slot context), `toast.bundle.import.roConfigReset`.

### Tests (`tests/FestivalRider.Tests`)

Test file paths follow the existing flat layout (service tests at root, migrator tests under `Migrators/`, bundle migrator tests under `BundleMigrators/`). Every fixture below MUST resolve services through their interfaces, fake `IJSRuntime` and time, and use `CultureInfo.InvariantCulture` for any literal date / numeric input.

#### `RunningOrderSchedulerTests.cs` — new

**Traditional pipeline.** Forward on-stage cascade per stage. Backward soundcheck packing from `min(EffectiveDoors, EffectiveFirstShow) - EffectiveBreakTime`. Break-time enforcement emits `BreakTimeViolation`. On-stage overlap on a single stage emits `OnStageOverlap`. Pin-vs-cascade emits `BarrierConflict` when a downstream pinned slot blocks the cascade and `BackwardLockConflict` when an upstream pinned slot blocks the backward soundcheck pack — assert both warning types fire from their respective scenarios so the two enum values are not collapsed in the implementation. `FirstShowTimeMissing` fallback uses `BaseDate.AddHours(20)` and is emitted **exactly once per `Recalculate`** even when multiple slots depend on it. `SoundcheckGapMinutes` separates same-stage consecutive soundchecks with both default (0) and overridden values. Soundcheck order override via `SoundcheckOrderIndex` reorders the backward pack independent of playing order. Soundcheck packing colliding with `EffectiveTechnicalGetIn` emits `SoundcheckShrunk`. User-added `LOAD_IN_VENUE` placement uses the venue-open / technical-get-in / `BaseDate+8h` fallback chain; `VenueClosed` fires when start is before `EffectiveVenueOpenTime` or end exceeds `EffectiveVenueCloseTime`, and is skipped when either bound is null. Two same-day bands have independent `LOAD_IN_VENUE` and `BACKSTAGE_DROP` events that may overlap without warning. User-added `BACKSTAGE_DROP` placement uses `max(EffectiveVenueOpenTime, LOAD_IN_VENUE.End)` when `LOAD_IN_VENUE` is present, then `SoundcheckStart - DefaultBackstageDropMinutes`, then `OnStageTime - 6h`. `SoundCurfewTime` violation emits `CurfewViolation` distinct from backstage-curfew violation; both variants ship covered scenarios including cross-day curfew at `02:00` of `BaseDate+1d`. Cross-day cascade — soundcheck on `BaseDate`, `ON_STAGE` on `BaseDate+1d` — round-trips through the scheduler without losing the day offset.

**Festival pipeline.** Template-driven chain growth from anchor for both `PreShowEntries` (reverse chronological) and `PostShowEntries` (chronological) with their default durations. Multi-stage independence — two stages with overlapping `ON_STAGE` times do NOT emit `OnStageOverlap`. Early-chain validation: `EarlyChain.End <= OnStageTime` on the same stage passes; violation emits `EarlySoundcheckAfterOnStage`. Anchor 3-step fallback exercised end-to-end: `ro.AnchorEventOverride` wins; falls through to `show.DefaultAnchorEvent`; falls through to `ON_STAGE`. Non-`ON_STAGE` anchor (e.g. `SOUNDCHECK`) drives the chain origin. `UserOverrideFlags.AllowSoundcheckOverlap` downgrades a soundcheck overlap to informational `UserOverrideOverlap`; `AllowOnStageOverlap` does the same for on-stage overlap; both bits set together downgrade both. Multiple `TimingChainEntry` instances of the same `EventType` in a chain are honoured (no de-duping). Empty `PreShowEntries` and empty `PostShowEntries` produce a chain with only the anchor event and no warnings. `TimingChainEntry.CustomDisplayName` is ignored by the scheduler — assert outputs are byte-identical when the custom name is set vs. unset. No-soundcheck via absence of a `SOUNDCHECK` entry in `PreShowEvents` (NOT a `BandScheduleFlags` value) skips the soundcheck pack for that slot only.

**Effective-value resolution.** Parametric theory enumerating every overridable scalar (`ScheduleMode`, `AnchorEvent`, `VenueOpenTime`, `VenueCloseTime`, `TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, `SoundCurfewTime`, `BackstageCurfewTime`, `BreakfastHours`, `LunchHours`, `DinnerHours`, `BreakTimeMinutes`, `SoundcheckGapMinutes`): RO override populated wins; RO override null falls through to show-level value; both null falls through to the documented final default (anchor `ON_STAGE`, first-show `BaseDate+20h`, others null with the corresponding check skipped or warning emitted). One row per scalar.

**`BackstageTime` derivation.** `earliest(CHANGEOVER.StartTime, PRESHOW_LINECHECK.StartTime, OnStageTime) - BackstageLeadMinutes` — assert all three present, only `OnStageTime` present (others drop out of the min), only `CHANGEOVER` present, only `PRESHOW_LINECHECK` present. `IsBackstageTimePinned = true` makes the stored value win regardless of derived value. `IsBackstageTimePinned` and `IsBackstageCurfewPinned` are independent — pinning one does not pin the other.

**`HasPersonalBackstageCurfew` flag semantics.** Bit clear + `Slot.BackstageCurfewTime` populated ⇒ slot value ignored, scheduler uses `EffectiveBackstageCurfew`. Bit set ⇒ slot value used. Behaviour orthogonal to `IsBackstageCurfewPinned`.

**Duration fallback.** `SlotTimingEvent.DurationMinutes = null` falls back to `VenueTimingOptions.Default*` in traditional mode and to the matching `FestivalTimingTemplate` entry default in festival mode. Non-null value on the event row wins over the default in both modes. `CHANGEOVER` and `PRESHOW_LINECHECK` durations come exclusively from their event rows (no slot-level scalar).

**Toggle-flip auto-seeding.** Flipping each `VenueTimingOptions.Include*` toggle from `false` → `true` between two `Recalculate` calls seeds a `SlotTimingEvent` with computed `StartTime` and `IsPinned = false` for every existing slot, for every toggle in `{IncludeGetIn, IncludeLoadInVenue, IncludeStageLoadIn, IncludeBackstageDrop, IncludeSetupOnStage, IncludeSoundcheck, IncludePreShowLinecheck}`. Flipping back to `false` removes the auto-seeded events but preserves any user-pinned event of the same type.

**API surface.** `AddSlot(BandPlacement)` mints a `Guid Id`, honours `InsertAtIndex`, applies `PinnedOnStageTime` (with `IsOnStagePinned = true`), and auto-seeds events for every venue/template-included type. `RemoveSlot(slotId)` removes only the matching slot; unknown `Guid` returns a `ScheduleResult` whose warnings flag the missing slot and leaves the graph untouched. `MoveSlot(slotId, newIndex)` reorders by stable id, never by list index — assert reorder still works after any prior `Insert`/`Remove`. `SetSoundcheckOrder(slotId, newIndex)` mutates only `SoundcheckOrderIndex` for slots on the same stage and triggers a backward re-pack. `Validate(ro, show)` is read-only — running it twice in a row leaves the graph byte-identical and returns the same warning set. Defensive stage handling: a slot whose `StageId` is not present in `show.Stages` is skipped without throwing, and a `ConstraintViolation` warning is emitted.

**Idempotency.** Two consecutive `Recalculate` calls with no intermediate user mutation yield (a) the same `RunningOrder` graph by structural equality including event ordering, and (b) the same `Warnings` set. Idempotency holds when slots have a mix of pinned and unpinned events; pinned events stay byte-identical.

**Catering-window check.** `Slot.CateringSlot` falling outside every active meal window for the day emits `CateringOutsideHours`. Point-in-time `CateringSlot` (`End == null`) is checked against meal windows by `Start` only. Point-in-time meal windows accept any catering slot whose `Start` matches. Empty meal windows on both show and RO levels disable the check (no warning).

#### `BandServiceTests.cs` — extend

`UpdateShow` regression — every new `ShowData` scalar (the 16 enumerated in the architecture rule) copies across; nested `Bands` and `RunningOrders` reference identity is preserved (plan-019 reparenting rule); a missed property in the implementation MUST fail this test. Multi-RO `DateOfOpening` shift — show meal `TimeSlot`s, every RO meal-hour override `TimeSlot`, and every `Slot.CateringSlot` across **all** `RunningOrders` shift by `(newDate - oldDate)` days; both `Start` and non-null `End` shift; point-in-time slots (`End == null`) shift cleanly; time-of-day is preserved. `UpdateRunningOrder` `ShowDayNumber` shift — RO meal overrides and slot catering shift by the day delta; show-level meal slots are untouched. `UpdateRunningOrder` replaces the entire `RunningOrder` graph including `VenueOptions` and `FestivalTemplate` references (existing references are dropped, not merged). `AddRunningOrder` continues to set `order.ShowId = ActiveShowId` (v5 invariant regression). `DeleteStage` removes the deleted `stageId` from every `ShowData.StageLinkGroups[*].StageIds` and drops any group whose remaining `StageIds.Count < 2`; groups with ≥ 2 remaining ids are kept with the stale id removed.

#### `ExportServiceTests.cs` — extend

New v6 column list round-trip is byte-stable. Implicit-stage column omission (when `show.Stages.Count == 0`) round-trips unchanged via `NoStageSlotRowMap`. Cross-day `OnStageDayOffset`, `BackstageDayOffset`, `BackstageCurfewDayOffset`, `CateringStartDayOffset`, `CateringEndDayOffset` integers round-trip with non-zero values. `Flags` and `OverrideFlags` columns round-trip multi-bit values as comma-separated enum names — at minimum exercise `OverrideFlags = AllowSoundcheckOverlap, AllowOnStageOverlap` and assert ordering matches enum declaration. Empty `Flags` / `OverrideFlags` decode to `None`. Point-in-time catering: `CateringStart` non-empty + `CateringEnd` empty round-trips as `TimeSlot { End = null }`; both empty + zero offset decodes to `CateringSlot = null`. Bundle import resolves rows to bands by **case-sensitive** name match — a row whose `BandName` differs only in casing is dropped with a warning, never silently merged. The v5 `ChangeoverMinutes` column is absent from v6 CSV; round-trip does NOT regress it.

#### `StorageServiceTests.cs` — extend

End-to-end v5 → v6 schema migration via `EnsureLoadedAsync`: a stored payload at `schemaVersion = 5` runs the `IStateMigrator` chain, persists the migrated payload back to `festivalrider.state`, and surfaces a `Migrated data v5 → v6.` toast through the fake toast service. Migrator chain failure (use a `ThrowingMigrator` injected after `V5ToV6Migrator`) copies the raw payload to `festivalrider.backup.v5`, resets to a clean `AppState`, and toasts; assert the backup key contains the original bytes. `festivalrider.locale` is untouched by the migration. Per-RO scalars and templates round-trip through `AppState` JSON across save/load (the bundle-CSV omission rule does NOT apply to direct state persistence).

#### `PrintStrategyTests.cs` — extend

Cross-day rendering in `StagePrintStrategy` and `RolePrintStrategy`: a slot whose `OnStageTime` lands at `00:30` of `BaseDate+1d` renders with the `+1d` suffix; same-day slots render without the suffix. Print strategies do not depend on `ScheduleGantt` — render output is unchanged when no Gantt component is present in the test host.

#### `Migrators/V5ToV6MigratorTests.cs` — new

Reverse-soundcheck-order computation: `SoundcheckOrderIndex = (slotCount - 1) - playingIndex` for every slot in playing-order. Fresh `Guid Id` minted per slot; uniqueness across all migrated slots in a payload. `OnStageTime` reconstruction from `show.DateOfOpening.AddDays(ro.ShowDayNumber - 1).ToDateTime(slot.StartTime)`; `IsOnStagePinned = true` set on every migrated slot. v5 `ChangeoverMinutes` lands inside a freshly seeded `CHANGEOVER` `SlotTimingEvent` in `PreShowEvents` with `DurationMinutes = v5ChangeoverMinutes`; the auto-seeded `PRESHOW_LINECHECK` event row has `DurationMinutes = null` (falls back to default at scheduler time). `BackstageLeadMinutes`, `BackstageTime`, `BackstageCurfewTime`, `CateringSlot` all left null. Default `VenueOptions` populated per RO with the documented duration scalars; `FestivalTemplate` left null; every `*Override` field left null. `ShowData` defaults stamped: `DefaultScheduleMode = Traditional`, `DefaultAnchorEvent = ON_STAGE`, `BreakTimeMinutes = 120`, `SoundcheckGapMinutes = 0`; the seven new `DateTime?` scalars and three meal-hour slots all null. `StageLinkGroups` cleanup: drops every `StageId` not in `ShowData.Stages`, then drops any group with fewer than 2 remaining ids; groups with ≥ 2 remaining ids survive with stale ids removed. `AppState.SchemaVersion` stamped to `6`. Idempotent re-run — running the migrator twice on a v5 payload yields a graph byte-identical to a single run. Edge cases: `RunningOrder` with empty `Slots` migrates without error; v5 slot with missing or null `notes` decodes as `Notes = null`; pinned and unpinned v5 semantics preserved (every migrated slot is `IsOnStagePinned = true` regardless of v5 input — document this explicitly).

#### `BundleMigrators/V5ToV6BundleMigratorTests.cs` — new

CSV column rewrite from the v5 list (`ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes,Notes`) to the v6 list (lock 23-column order). Defaults applied per row: `OnStageTime = StartTime` passthrough, `OnStageDayOffset = 0`, `IsOnStagePinned = true`; `BackstageLeadMinutes`, `BackstageTime`, `BackstageDayOffset`, `IsBackstageTimePinned`, `BackstageCurfewTime`, `BackstageCurfewDayOffset`, `IsBackstageCurfewPinned`, `CateringStart`, `CateringStartDayOffset`, `CateringEnd`, `CateringEndDayOffset`, `Flags`, `OverrideFlags` all empty; `SoundcheckOrderIndex = (rowCount - 1) - rowPosition`. Fresh `Guid Id` per row, unique. Manifest `schemaVersion` rewritten to `6`. v5 `ChangeoverMinutes` column dropped from CSV; on subsequent `BundleService.ImportBundle` decode, `BundleService` re-seeds the `CHANGEOVER` `SlotTimingEvent` with `DurationMinutes = null` (default fallback) — assert via a paired integration test that the imported `RunningOrderSlot.PreShowEvents` contains a `CHANGEOVER` row with `null` duration. Per-RO scalars and templates absent from CSV; on import they reset to defaults and `BundleImportResult.Warnings` contains a `toast.bundle.import.roConfigReset` entry. Manifest `bands` and `show` entries round-trip byte-identical when only the running-order CSV needs migrating.

#### `LocalizationCatalogTests.cs` — extend

Enum-key-prefix counts: `enum.TimingEventType.*` exactly 13, `enum.ScheduleMode.*` exactly 2, `enum.ScheduleWarningType.*` exactly 14. Enumerated key-presence assertions for the non-enum keys introduced by the plan: every `page.runningOrder.toolbar.*` label, every `page.runningOrder.gantt.*` and `gantt.legend.*`, every `page.runningOrder.panel.*`, every `page.runningOrder.template.*`, every `page.runningOrder.venueOptions.*` (including `.includeLoadInVenue`, `.includeBackstageDrop`, `.defaultLoadInVenueMinutes`, `.defaultBackstageDropMinutes`), and `toast.schedule.warning` + `toast.bundle.import.roConfigReset`. Assertions run against both `en.json` and `fr-fr.json` so a missing translation in either locale fails the suite (the existing parity test catches mismatches but not omissions present in both files).

#### `EnumShapeTests.cs` — new

Structural invariants enforcing the locked enum surface: `BandScheduleFlags` contains exactly `{None, HasPersonalBackstageCurfew}` and explicitly does NOT define `Headliner`, `EarlySoundcheck`, or `NoSoundcheck`. `UserOverrideFlags` contains exactly `{None, AllowSoundcheckOverlap, AllowOnStageOverlap}`. `TimingEventType` has exactly 13 values matching the locked list, in declaration order, and explicitly does NOT define `FREETIME`. `ScheduleMode` has exactly `{Traditional, Festival}`. `ScheduleWarningType` has exactly 14 values matching the locked list. Each assertion uses `Enum.GetNames` against a hard-coded expected array.

#### `ProgramRegistrationTests.cs` — new (or fold into existing DI smoke fixture)

Resolution smoke tests built off a `WebAssemblyHostBuilder` analogue or a manual `ServiceCollection` mirroring `Program.cs`: `IRunningOrderScheduler` resolves to `RunningOrderScheduler` with `Scoped` lifetime; the `IEnumerable<IStateMigrator>` collection contains an instance of `V5ToV6Migrator` in addition to every prior migrator; the `IEnumerable<IBundleMigrator>` collection contains `V5ToV6BundleMigrator`. Failure mode covered: a missed `AddScoped` registration in `Program.cs` MUST fail this fixture.

#### `TestDataFactory.cs` — extend

Extend `FullShow()` to seed sane scheduler defaults (non-null `VenueOpenTime`, `VenueCloseTime`, `TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, three meal slots, `BreakTimeMinutes = 120`, `SoundcheckGapMinutes = 0`). Add `TraditionalRunningOrder()` factory with default `VenueOptions` and a small slot list pinned at `ON_STAGE`. Add `FestivalRunningOrder()` factory with a `FestivalTimingTemplate` populated for the "Festival Main Stage" preset and a multi-stage slot list. Both factories return a graph that satisfies `Recalculate` with zero warnings, so warning-emission tests can isolate the variable under test.

## Task order

Each step MUST leave the app compiling. Tests targeting modified surfaces MUST stay green at the end of every step that touches them.

1. **Models + ExportService minimal compile-fix** — Add every new model file. Convert `RunningOrderSlot` from record to class (add `Guid Id`; do NOT add `ChangeoverMinutes` or `PreShowLinecheckMinutes` scalars). Update `RunningOrder`, `ShowData`, `AppState`. Update `ExportService` (new column list, full writer + reader), `BundleService`, `BandService.UpdateShow`, `RunningOrderV2.razor` (temporary scaffolding so the page compiles), `RunningOrderSlotRow.razor` (temporary patch — deleted in task 6), `StagePrintStrategy`, `RolePrintStrategy` (use `slot.OnStageTime.TimeOfDay` for now). Update consumer tests.
2. **`IRunningOrderScheduler` + Traditional algorithm** — Define interface, implement Traditional mode end-to-end. Register `Scoped` in `Program.cs`. Add Traditional-mode flows to `RunningOrderSchedulerTests`.
3. **`V5ToV6Migrator` + `V5ToV6BundleMigrator`** — Implement both. Register in `Program.cs`. Add `V5ToV6MigratorTests` and `V5ToV6BundleMigratorTests`.
4. **Festival mode** — Implement `ComputeFestivalTimeline`, anchor/template growth, early-chain validation. Extend `RunningOrderSchedulerTests`.
5. **Localization** — Add every new key to `en.json`, `fr-fr.json`, `LocalizationKeys.cs`. `LocalizationCatalogTests` green.
6. **UI components** — Implement `ScheduleGantt`, `ScheduleBandPanel`, `TemplateEditor`, `VenueOptionsEditor`. Rewrite `RunningOrderV2.razor` to host them. **Delete `RunningOrderSlotRow.razor`**.
7. **Print strategy cross-day update** — Replace the `slot.OnStageTime.TimeOfDay` quick-fix from task 1 with cross-day-aware rendering (`+Nd` suffix) in `StagePrintStrategy` and `RolePrintStrategy`.
8. **Preset templates + override polish** — Add the three preset buttons in `TemplateEditor`. Wire `UserOverrideFlags` "Allow overlap" buttons in `ScheduleGantt` / `ScheduleBandPanel`.
9. **Integration sweep** — Create a show, add bands, switch modes, exercise both timelines, export and import a bundle, verify v5 state migrates cleanly. Commit.

## Implementation cadence

- **Wave 1 — Models + CSV byte stability + Traditional scheduler** (tasks 1–2). Demoable: traditional running order auto-computes soundcheck and on-stage times; CSV round-trips byte-stably.
- **Wave 2 — Migration** (task 3). Demoable: load v5 state, save, reload as v6; v5 bundle imports into a v6 app.
- **Wave 3 — Festival scheduler + localization** (tasks 4–5). Demoable: switch a running order to festival mode via JSON edit, see chains computed.
- **Wave 4 — UI + print + presets** (tasks 6–8). Demoable: full Gantt + side panel + template editor with presets; print strategies render cross-day timing correctly.
- **Wave 5 — Integration verification** (task 9).

## Out of scope

- Storage-space conflict checks for `LOAD_IN_VENUE` (user-managed; scheduler only validates the `VenueOpenTime` / `VenueCloseTime` window).
- Stage linking UI and enforcement (schema only in v6; scheduler ignores `StageLinkGroups`).
- Auto-packing soundchecks into festival-mode gaps.
- Drag-to-resize duration on the Gantt.
- Live cross-tab sync of running-order edits.
- PDF export of the Gantt chart.
- Multi-select bulk operations on the Gantt.
- Per-RO scalar and template round-trip via bundle CSV (JSON-only in v6).

## Risks & migrations

- **Legacy `RunningOrderSlot` record → class** — v5 records are immutable; v6 instances are mutable with a `Guid Id`. `V5ToV6Migrator` reads raw JSON properties (`startTime`, `setLengthMinutes`, `changeoverMinutes`, `notes`) and constructs new instances; it MUST NOT reference the old record type at compile time. Tests cover empty slots, missing notes, pinned/unpinned semantics.
- **CSV column expansion** — v6 lock amends the AGENTS.md root rule. Writer, reader, and `ExportServiceTests` ship together. Cross-day data round-trips via `*DayOffset` integer columns.
- **`DateTime` vs `TimeOnly` in print strategies** — task 1 uses `slot.OnStageTime.TimeOfDay` as a quick-fix; task 7 hardens with cross-day rendering. Print strategies stay independent of the Gantt component.
- **Per-RO scalar and template loss on bundle round-trip** — v6 bundle CSV omits them; import resets to defaults. `toast.bundle.import.roConfigReset` notifies the user. Successor plan may extend the bundle wire format.
- **`StageLinkGroups` orphan IDs** — `BandService.DeleteStage` prunes orphaned ids and drops groups with `< 2` remaining ids; `V5ToV6Migrator` performs the same cleanup on legacy state. Scheduler additionally skips non-existent stages defensively. v6 still ignores the structure for scheduling.
- **`BandService.UpdateShow` silently dropping new scalars** — the architecture rule enumerates every property to copy. Regression test in `BandServiceTests`.
- **Catering anchoring** — `TimeSlot` values are absolute `DateTime` referenced to `BaseDate`. `BandService.UpdateShow` / `UpdateRunningOrder` shift them programmatically when `DateOfOpening` / `ShowDayNumber` change; tests assert preservation of time-of-day.
