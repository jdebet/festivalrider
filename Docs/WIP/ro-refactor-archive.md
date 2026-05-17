# 020 — Running order schedule refactor

## Status

Draft

## Context

Successor to [019-show-scoped-bands.md](./019-show-scoped-bands.md). Plan 019 made shows self-contained (bands, running orders, stages) but left the running order as a simple table of `StartTime`/`SetLength`/`Changeover` records with no automatic timing logic. Real-world usage requires two distinct workflows: a traditional venue timeline (soundchecks packed before doors, sequential on-stage times) and a festival timeline (interleaved stages, early soundchecks, independent stage use, per-band event chains). This plan replaces the flat `RunningOrderSlot` record with a rich event-chain model, introduces a dual-mode scheduler, and adds an interactive Gantt + side-panel UI.

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
- **Single source of truth for canonical durations** — `RunningOrderSlot` exposes `ChangeoverMinutes` and `PreShowLinecheckMinutes` as direct properties. These are the canonical values used by the on-stage forward cascade. The `PreShowEvents` entries for `CHANGEOVER` and `PRESHOW_LINECHECK` read from these properties (or template defaults if null); they do NOT store independent copies. The side panel edits the canonical properties directly.
- **Catering TimeSlot anchoring** — `TimeSlot.Start`/`End` for catering are absolute `DateTime` values anchored to the show's `BaseDate`. Changing `DateOfOpening` or `ShowDayNumber` does NOT automatically shift catering times; the user adjusts them manually. The UI displays catering times as time-only (e.g. "12:00") with an implicit date.
- **Orphaned stage link IDs** — `StageLinkGroup.StageIds` are NOT auto-cleaned when a stage is deleted. Orphaned IDs are harmless (scheduler skips non-existent stages) but may produce stale warnings. Cleanup deferred to a successor plan.

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
- `RunningOrderSlot` MUST expose `ChangeoverMinutes` and `PreShowLinecheckMinutes` as direct nullable properties. The `PreShowEvents` entries for `CHANGEOVER` and `PRESHOW_LINECHECK` derive their durations from these properties (falling back to template defaults). They MUST NOT hold their own independent duration copies.
- `RunningOrderSlot` MUST expose `SoundcheckOrderIndex` as an `int` (default 0). The scheduler packs soundchecks in ascending index order within each stage group. The default value for a newly added slot MUST be computed as the reverse of the playing order (last band gets 0, second-to-last gets 1, etc.).

## File-by-file scope

### Models (`src/FestivalRider/Models`)

#### `TimeSlot.cs`

```csharp
namespace FestivalRider.Models;

public class TimeSlot
{
    public DateTime Start { get; set; }
    public DateTime? End { get; set; } // null = point-in-time
}
```

#### `TimingEventType.cs`

```csharp
namespace FestivalRider.Models;

public enum TimingEventType
{
    GET_IN,
    LOAD_IN_VENUE,
    LOAD_IN_STAGE,
    BACKSTAGE_DROP,
    CATERING,
    SETUP_ON_STAGE,
    SOUNDCHECK,
    CHANGEOVER,
    PRESHOW_LINECHECK,
    ON_STAGE,
    LOAD_OUT_STAGING,
    LOAD_OUT_VENUE,
    BACKSTAGE_WAIT,
}
```

#### `ScheduleMode.cs`

```csharp
namespace FestivalRider.Models;

public enum ScheduleMode
{
    Traditional,
    Festival,
}
```

#### `StageLinkConstraint.cs`

```csharp
namespace FestivalRider.Models;

public enum StageLinkConstraint
{
    All,
    OnStageOnly,
}
```

#### `StageLinkGroup.cs`

```csharp
namespace FestivalRider.Models;

public class StageLinkGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<int> StageIds { get; set; } = new();
    public StageLinkConstraint Constraint { get; set; } = StageLinkConstraint.All;
}
```

#### `TimingChainEntry.cs`

```csharp
namespace FestivalRider.Models;

public class TimingChainEntry
{
    public TimingEventType EventType { get; set; }
    public string? CustomDisplayName { get; set; } // null = use default localized name
    public int DefaultDurationMinutes { get; set; }
    public bool IsOptional { get; set; }
}
```

#### `FestivalTimingTemplate.cs`

```csharp
namespace FestivalRider.Models;

public class FestivalTimingTemplate
{
    // Early morning chain for headliners (optional per band)
    public List<TimingChainEntry> EarlyChain { get; set; } = new();

    // Main pre-show chain, in REVERSE chronological order.
    // Index 0 = event closest to ON_STAGE.
    public List<TimingChainEntry> PreShowEntries { get; set; } = new();

    // Post-show chain, in chronological order.
    public List<TimingChainEntry> PostShowEntries { get; set; } = new();

    public int DefaultSetLengthMinutes { get; set; } = 45;
}
```

#### `VenueTimingOptions.cs`

```csharp
namespace FestivalRider.Models;

public class VenueTimingOptions
{
    // Which events exist in the fixed pre-show chain
    public bool IncludeGetIn { get; set; } = true;
    public bool IncludeStageLoadIn { get; set; } = true;
    public bool IncludeSetupOnStage { get; set; } = true;
    public bool IncludeSoundcheck { get; set; } = true;
    public bool IncludePreShowLinecheck { get; set; } = true;

    // Default durations for the fixed pre-show chain
    public int DefaultGetInMinutes { get; set; } = 30;
    public int DefaultStageLoadInMinutes { get; set; } = 30;
    public int DefaultSetupOnStageMinutes { get; set; } = 45;
    public int DefaultSoundcheckMinutes { get; set; } = 90;
    public int DefaultPreShowLinecheckMinutes { get; set; } = 10;
    public int DefaultBackstageLeadMinutes { get; set; } = 10;
    public int DefaultChangeoverMinutes { get; set; } = 20;
    public int DefaultSetLengthMinutes { get; set; } = 45;
}
```

#### `SlotTimingEvent.cs`

```csharp
namespace FestivalRider.Models;

public class SlotTimingEvent
{
    public TimingEventType EventType { get; set; }
    public DateTime? StartTime { get; set; }
    public int? DurationMinutes { get; set; } // null = use template default
    public bool IsPinned { get; set; }
}
```

#### `BandScheduleFlags.cs`

```csharp
namespace FestivalRider.Models;

[Flags]
public enum BandScheduleFlags
{
    None = 0,
    HasPersonalBackstageCurfew = 1,
}
```

#### `UserOverrideFlags.cs`

```csharp
namespace FestivalRider.Models;

[Flags]
public enum UserOverrideFlags
{
    None = 0,
    AllowSoundcheckOverlap = 1,
    AllowOnStageOverlap = 2,
}
```

#### `ScheduleWarningType.cs`

```csharp
namespace FestivalRider.Models;

public enum ScheduleWarningType
{
    BreakTimeViolation,
    SoundcheckBlockOverlap,
    OnStageOverlap,
    BackwardLockConflict,
    BarrierConflict,
    CateringOutsideHours,
    CurfewViolation,
    SoundcheckShrunk,
    SoundcheckOrderOverlap,
    UserOverrideOverlap,
    EarlySoundcheckAfterOnStage,
    ConstraintViolation,
    FirstShowTimeMissing,
}
```

#### `ScheduleWarning.cs`

```csharp
namespace FestivalRider.Models;

public class ScheduleWarning
{
    public ScheduleWarningType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? SlotIndex { get; set; }
    public int? RelatedSlotIndex { get; set; }
}
```

#### `ScheduleResult.cs`

```csharp
namespace FestivalRider.Models;

public class ScheduleResult
{
    public bool Success { get; set; }
    public List<ScheduleWarning> Warnings { get; set; } = new();
}
```

#### `RunningOrderSlot.cs`

Replaces the existing `record RunningOrderSlot(...)`.

```csharp
using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class RunningOrderSlot
{
    public Guid BandId { get; set; }
    public int StageId { get; set; }

    // --- Anchor ---
    public DateTime? OnStageTime { get; set; }
    public bool IsOnStagePinned { get; set; }

    // --- Set ---
    public int? SetLengthMinutes { get; set; }

    // --- Canonical durations for the on-stage forward cascade ---
    // These are the single source of truth. The PreShowEvents entries
    // for CHANGEOVER and PRESHOW_LINECHECK derive from these values.
    public int? ChangeoverMinutes { get; set; }
    public int? PreShowLinecheckMinutes { get; set; }

    // --- Soundcheck packing order (traditional mode) ---
    // Default = reverse of playing order. 0 = first soundcheck of the day.
    public int SoundcheckOrderIndex { get; set; }

    // --- Festival: early soundcheck chain ---
    public List<SlotTimingEvent> EarlyChain { get; set; } = new();

    // --- Main pre-show chain (reverse chronological; index 0 closest to OnStage) ---
    public List<SlotTimingEvent> PreShowEvents { get; set; } = new();

    // --- Post-show chain (chronological) ---
    public List<SlotTimingEvent> PostShowEvents { get; set; } = new();

    // --- Derived: backstage presence time ---
    // For first band: OnStageTime - BackstageLeadMinutes.
    // For subsequent: min(previousEnd - BackstageLeadMinutes, LinecheckTime).
    public DateTime? BackstageTime { get; set; }
    public int? BackstageLeadMinutes { get; set; }
    public bool IsBackstageTimePinned { get; set; }

    // --- Per-band constraint ---
    public DateTime? BackstageCurfewTime { get; set; }
    public bool IsBackstageCurfewPinned { get; set; }

    // --- Flags ---
    public BandScheduleFlags Flags { get; set; }
    public UserOverrideFlags OverrideFlags { get; set; }

    // --- Catering ---
    public TimeSlot? CateringSlot { get; set; }

    public string? Notes { get; set; }
}
```

#### `RunningOrder.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class RunningOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShowId { get; set; }

    [Range(1, 31)]
    public int ShowDayNumber { get; set; } = 1;

    // --- Mode and anchor overrides ---
    public ScheduleMode? ModeOverride { get; set; }
    public TimingEventType? AnchorEventOverride { get; set; }

    // --- Timing overrides (null = inherit from ShowData) ---
    public DateTime? TechnicalGetInTimeOverride { get; set; }
    public DateTime? DoorsOpeningTimeOverride { get; set; }
    public DateTime? FirstShowTimeOverride { get; set; }
    public DateTime? SoundCurfewTimeOverride { get; set; }
    public DateTime? BackstageCurfewTimeOverride { get; set; }
    public TimeSlot? BreakfastHoursOverride { get; set; }
    public TimeSlot? LunchHoursOverride { get; set; }
    public TimeSlot? DinnerHoursOverride { get; set; }
    public int? BreakTimeMinutesOverride { get; set; }
    public int? SoundcheckGapMinutesOverride { get; set; }

    // --- Mode-specific config ---
    public VenueTimingOptions? VenueOptions { get; set; }
    public FestivalTimingTemplate? FestivalTemplate { get; set; }

    public List<RunningOrderSlot> Slots { get; set; } = new();
}
```

#### `ShowData.cs`

Additions to the existing class:

```csharp
public class ShowData
{
    // ... existing fields: Id, Name, Address, DateOfOpening, ShowDayCount,
    //     Stages, Bands, RunningOrders ...

    // --- Schedule mode defaults ---
    public ScheduleMode DefaultScheduleMode { get; set; } = ScheduleMode.Traditional;
    public TimingEventType DefaultAnchorEvent { get; set; } = TimingEventType.ON_STAGE;

    // --- Global timing anchors (nullable = not set) ---
    public DateTime? TechnicalGetInTime { get; set; }
    public DateTime? DoorsOpeningTime { get; set; }
    public DateTime? FirstShowTime { get; set; }
    public DateTime? SoundCurfewTime { get; set; }
    public DateTime? BackstageCurfewTime { get; set; }

    // --- Catering windows ---
    public TimeSlot? BreakfastHours { get; set; }
    public TimeSlot? LunchHours { get; set; }
    public TimeSlot? DinnerHours { get; set; }

    // --- Break time between last soundcheck and doors/first show ---
    public int BreakTimeMinutes { get; set; } = 120;

    // --- Gap between consecutive soundcheck blocks on same stage ---
    public int SoundcheckGapMinutes { get; set; } = 0;

    // --- Stage linking ---
    public List<StageLinkGroup> StageLinkGroups { get; set; } = new();
}
```

#### `AppState.cs`

Bump schema version:

```csharp
public class AppState
{
    public int SchemaVersion { get; set; } = 6;
    // ... rest unchanged ...
}
```

### Services (`src/FestivalRider/Services`)

#### `BandPlacement.cs`

```csharp
namespace FestivalRider.Services;

public class BandPlacement
{
    public Guid BandId { get; set; }
    public int StageId { get; set; }
    public int? InsertAtIndex { get; set; }       // "this band plays Nth on this stage"
    public DateTime? PinnedOnStageTime { get; set; } // "this band goes on at exactly this time"
}
```

#### `IRunningOrderScheduler.cs`

```csharp
using FestivalRider.Models;

namespace FestivalRider.Services;

public interface IRunningOrderScheduler
{
    // Full recalculate. Mutates the RO in-place. Returns warnings.
    ScheduleResult Recalculate(RunningOrder order, ShowData show);

    // Add a band to the schedule.
    ScheduleResult AddBand(RunningOrder order, BandPlacement placement, ShowData show);

    // Remove a band and recalc the cascade.
    ScheduleResult RemoveBand(RunningOrder order, int slotIndex, ShowData show);

    // Move a band in the on-stage playing order (per stage).
    ScheduleResult MoveBand(RunningOrder order, int fromIndex, int toIndex, ShowData show);

    // Reorder soundchecks independently of playing order (traditional mode).
    ScheduleResult SetSoundcheckOrder(RunningOrder order, int slotIndex, int newSoundcheckIndex, ShowData show);

    // Validate without mutating.
    List<ScheduleWarning> Validate(RunningOrder order, ShowData show);
}
```

#### `RunningOrderScheduler.cs`

This is the core service. It contains two private algorithm methods plus shared helpers.

```csharp
using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

public class RunningOrderScheduler : IRunningOrderScheduler
{
    private readonly ILogger<RunningOrderScheduler> _logger;

    public RunningOrderScheduler(ILogger<RunningOrderScheduler> logger)
    {
        _logger = logger;
    }

    public ScheduleResult Recalculate(RunningOrder order, ShowData show)
    {
        var mode = order.ModeOverride ?? show.DefaultScheduleMode;
        return mode == ScheduleMode.Traditional
            ? ComputeTraditionalTimeline(order, show)
            : ComputeFestivalTimeline(order, show);
    }

    // --- Traditional mode ---

    private ScheduleResult ComputeTraditionalTimeline(RunningOrder order, ShowData show)
    {
        var warnings = new List<ScheduleWarning>();

        // Step 1: Resolve effective globals
        var effectiveDoors = order.DoorsOpeningTimeOverride ?? show.DoorsOpeningTime;
        var effectiveFirstShow = order.FirstShowTimeOverride ?? show.FirstShowTime;
        var effectiveCurfew = order.SoundCurfewTimeOverride ?? show.SoundCurfewTime;
        var effectiveBreak = order.BreakTimeMinutesOverride ?? show.BreakTimeMinutes;
        var effectiveGap = order.SoundcheckGapMinutesOverride ?? show.SoundcheckGapMinutes;
        var options = order.VenueOptions ?? new VenueTimingOptions();

        // Fallback for missing FirstShowTime
        var baseDate = show.DateOfOpening.AddDays(order.ShowDayNumber - 1);
        if (effectiveFirstShow is null)
        {
            effectiveFirstShow = baseDate.AddHours(20);
            warnings.Add(new ScheduleWarning
            {
                Type = ScheduleWarningType.FirstShowTimeMissing,
                Message = "First show time not set; fell back to 20:00."
            });
        }

        // Step 2: On-stage cascade (forward, per stage)
        var slotsByStage = order.Slots.GroupBy(s => s.StageId).ToList();
        foreach (var stageGroup in slotsByStage)
        {
            var slots = stageGroup.OrderBy(s => order.Slots.IndexOf(s)).ToList();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (i == 0)
                {
                    slot.OnStageTime ??= effectiveFirstShow;
                }
                else
                {
                    var prev = slots[i - 1];
                    var changeover = slot.ChangeoverMinutes ?? options.DefaultChangeoverMinutes;
                    var linecheck = slot.PreShowLinecheckMinutes ?? options.DefaultPreShowLinecheckMinutes;
                    var computedOnStage = prev.OnStageTime!.Value.AddMinutes(
                        (prev.SetLengthMinutes ?? options.DefaultSetLengthMinutes) + changeover + linecheck);

                    if (slot.IsOnStagePinned)
                    {
                        if (computedOnStage > slot.OnStageTime!.Value)
                        {
                            // Pinned time is earlier than computed cascade; barrier hit
                            warnings.Add(new ScheduleWarning
                            {
                                Type = ScheduleWarningType.BarrierConflict,
                                SlotIndex = order.Slots.IndexOf(slot),
                                Message = $"Pinned on-stage time conflicts with previous band's cascade."
                            });
                        }
                        else
                        {
                            // Pinned time is later; accept it, subsequent bands recalc
                            slot.OnStageTime = computedOnStage;
                        }
                    }
                    else
                    {
                        slot.OnStageTime = computedOnStage;
                    }
                }

                // Step 3: Backward pre-show events from OnStageTime
                var linecheckDuration = slot.PreShowLinecheckMinutes ?? options.DefaultPreShowLinecheckMinutes;
                var linecheckTime = slot.OnStageTime.Value.AddMinutes(-linecheckDuration);

                var changeoverDuration = slot.ChangeoverMinutes ?? options.DefaultChangeoverMinutes;
                var changeoverStart = linecheckTime.AddMinutes(-changeoverDuration);

                // Soundcheck block (if included)
                if (options.IncludeSoundcheck)
                {
                    var soundcheckDuration = slot.SoundcheckOrderIndex /* read from options or slot */;
                    // ... actual implementation continues with backward packing from deadline ...
                }

                // Step 4: Derived BackstageTime
                if (i == 0)
                {
                    var lead = slot.BackstageLeadMinutes ?? options.DefaultBackstageLeadMinutes;
                    slot.BackstageTime = slot.OnStageTime.Value.AddMinutes(-lead);
                }
                else
                {
                    var prev = slots[i - 1];
                    var lead = slot.BackstageLeadMinutes ?? options.DefaultBackstageLeadMinutes;
                    var fromPreviousEnd = prev.OnStageTime!.Value.AddMinutes(
                        prev.SetLengthMinutes ?? options.DefaultSetLengthMinutes).AddMinutes(-lead);
                    slot.BackstageTime = fromPreviousEnd < linecheckTime ? fromPreviousEnd : linecheckTime;
                }
            }
        }

        // Step 5: Soundcheck packing (backward from deadline, per stage)
        // ... deadline = min(effectiveDoors, firstOnStageOnStage) - effectiveBreak ...
        // ... pack in SoundcheckOrderIndex order, separated by effectiveGap ...

        // Step 6: Overlap checks and global constraints
        // ... per-stage checks, linked stage checks, curfew checks ...

        return new ScheduleResult { Success = true, Warnings = warnings };
    }

    // --- Festival mode ---

    private ScheduleResult ComputeFestivalTimeline(RunningOrder order, ShowData show)
    {
        var warnings = new List<ScheduleWarning>();
        var template = order.FestivalTemplate ?? new FestivalTimingTemplate();
        var anchor = order.AnchorEventOverride ?? show.DefaultAnchorEvent;

        // Resolve effective globals
        var effectiveFirstShow = order.FirstShowTimeOverride ?? show.FirstShowTime;
        var effectiveCurfew = order.SoundCurfewTimeOverride ?? show.SoundCurfewTime;
        var baseDate = show.DateOfOpening.AddDays(order.ShowDayNumber - 1);
        if (effectiveFirstShow is null)
        {
            effectiveFirstShow = baseDate.AddHours(20);
            warnings.Add(new ScheduleWarning
            {
                Type = ScheduleWarningType.FirstShowTimeMissing,
                Message = "First show time not set; fell back to 20:00."
            });
        }

        // On-stage cascade (same as traditional for first pass)
        var slotsByStage = order.Slots.GroupBy(s => s.StageId).ToList();
        foreach (var stageGroup in slotsByStage)
        {
            var slots = stageGroup.OrderBy(s => order.Slots.IndexOf(s)).ToList();
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (i == 0)
                {
                    slot.OnStageTime ??= effectiveFirstShow;
                }
                else
                {
                    var prev = slots[i - 1];
                    var changeover = slot.ChangeoverMinutes ?? template.DefaultSetLengthMinutes /* or template entry */;
                    var linecheck = slot.PreShowLinecheckMinutes ?? 10;
                    slot.OnStageTime = prev.OnStageTime!.Value.AddMinutes(
                        (prev.SetLengthMinutes ?? template.DefaultSetLengthMinutes) + changeover + linecheck);
                }

                // Grow PreShowEvents backward from anchor
                var anchorTime = slot.OnStageTime!.Value;
                var currentTime = anchorTime;
                foreach (var entry in slot.PreShowEvents)
                {
                    var duration = entry.DurationMinutes ?? template.PreShowEntries
                        .First(e => e.EventType == entry.EventType).DefaultDurationMinutes;
                    entry.StartTime = currentTime.AddMinutes(-duration);
                    if (entry.IsPinned)
                    {
                        entry.StartTime = entry.StartTime; // use pinned value
                        currentTime = entry.StartTime.Value;
                    }
                    else
                    {
                        currentTime = entry.StartTime.Value;
                    }
                }

                // Grow PostShowEvents forward from set end
                var setEnd = anchorTime.AddMinutes(slot.SetLengthMinutes ?? template.DefaultSetLengthMinutes);
                currentTime = setEnd;
                foreach (var entry in slot.PostShowEvents)
                {
                    var duration = entry.DurationMinutes ?? template.PostShowEntries
                        .First(e => e.EventType == entry.EventType).DefaultDurationMinutes;
                    entry.StartTime = currentTime;
                    if (entry.IsPinned)
                    {
                        entry.StartTime = entry.StartTime;
                        currentTime = entry.StartTime.Value.AddMinutes(duration);
                    }
                    else
                    {
                        currentTime = entry.StartTime.Value.AddMinutes(duration);
                    }
                }

                // Validate EarlyChain
                if (slot.EarlyChain.Count > 0)
                {
                    var earlyEnd = slot.EarlyChain.Last().StartTime!.Value.AddMinutes(
                        slot.EarlyChain.Last().DurationMinutes ?? 0);
                    if (earlyEnd > slot.OnStageTime.Value)
                    {
                        warnings.Add(new ScheduleWarning
                        {
                            Type = ScheduleWarningType.EarlySoundcheckAfterOnStage,
                            SlotIndex = order.Slots.IndexOf(slot),
                            Message = "Early soundcheck chain ends after on-stage time."
                        });
                    }
                }
            }
        }

        // Overlap checks: same-stage only for festival mode
        // ... validate no on-stage overlaps, no soundcheck→on-stage same-stage overlaps ...

        return new ScheduleResult { Success = true, Warnings = warnings };
    }

    // --- Public orchestration methods ---

    public ScheduleResult AddBand(RunningOrder order, BandPlacement placement, ShowData show)
    {
        // ... create slot, insert at placement.InsertAtIndex or set PinnedOnStageTime ...
        return Recalculate(order, show);
    }

    public ScheduleResult RemoveBand(RunningOrder order, int slotIndex, ShowData show)
    {
        if (slotIndex < 0 || slotIndex >= order.Slots.Count)
            return new ScheduleResult { Success = false, Warnings = new() };
        order.Slots.RemoveAt(slotIndex);
        return Recalculate(order, show);
    }

    public ScheduleResult MoveBand(RunningOrder order, int fromIndex, int toIndex, ShowData show)
    {
        if (fromIndex < 0 || fromIndex >= order.Slots.Count) return new() { Success = false };
        if (toIndex < 0 || toIndex >= order.Slots.Count) return new() { Success = false };
        var slot = order.Slots[fromIndex];
        order.Slots.RemoveAt(fromIndex);
        order.Slots.Insert(toIndex, slot);
        return Recalculate(order, show);
    }

    public ScheduleResult SetSoundcheckOrder(RunningOrder order, int slotIndex, int newSoundcheckIndex, ShowData show)
    {
        if (slotIndex < 0 || slotIndex >= order.Slots.Count) return new() { Success = false };
        order.Slots[slotIndex].SoundcheckOrderIndex = newSoundcheckIndex;
        return Recalculate(order, show);
    }

    public List<ScheduleWarning> Validate(RunningOrder order, ShowData show)
    {
        var result = Recalculate(order, show);
        return result.Warnings;
    }
}
```

> **Note:** The above is pseudocode-level C# for the plan. The actual implementation will fill in the soundcheck backward-packing logic, overlap check helpers, and constraint validation. The scheduler MUST keep `ComputeTraditionalTimeline` and `ComputeFestivalTimeline` as separate private methods.

#### `IBandService.cs` and `BandService.cs`

`BandService.UpdateShow` MUST copy all new scalar fields:

```csharp
public Task UpdateShow(ShowData show)
{
    if (show is null) throw new ArgumentNullException(nameof(show));
    var index = _state.Shows.FindIndex(s => s.Id == show.Id);
    if (index < 0) throw new InvalidOperationException($"Show {show.Id} not found.");
    var existing = _state.Shows[index];

    // Existing scalars
    existing.Name = show.Name;
    existing.Address = show.Address;
    existing.DateOfOpening = show.DateOfOpening;
    existing.ShowDayCount = show.ShowDayCount;
    existing.Stages = show.Stages;

    // NEW: schedule scalars
    existing.DefaultScheduleMode = show.DefaultScheduleMode;
    existing.DefaultAnchorEvent = show.DefaultAnchorEvent;
    existing.TechnicalGetInTime = show.TechnicalGetInTime;
    existing.DoorsOpeningTime = show.DoorsOpeningTime;
    existing.FirstShowTime = show.FirstShowTime;
    existing.SoundCurfewTime = show.SoundCurfewTime;
    existing.BackstageCurfewTime = show.BackstageCurfewTime;
    existing.BreakTimeMinutes = show.BreakTimeMinutes;
    existing.SoundcheckGapMinutes = show.SoundcheckGapMinutes;
    existing.StageLinkGroups = show.StageLinkGroups ?? new();

    // Preserve existing Bands and RunningOrders lists
    Raise();
    return Task.CompletedTask;
}
```

### Pages (`src/FestivalRider/Pages`)

#### `RunningOrderV2.razor`

Orchestrator. Layout:

- Top toolbar: mode selector (`Traditional`/`Festival`), `DoorsOpeningTime` input, `FirstShowTime` input, `SoundCurfewTime` input, `BackstageCurfewTime` input, `BreakTimeMinutes` input, `SoundcheckGapMinutes` input.
- Warning banner: displays `ScheduleResult.Warnings` as dismissible badges.
- Main area: `ScheduleGantt` on left (or top), `ScheduleBandPanel` on right (or bottom).
- Modal: `TemplateEditor` (festival mode only), `VenueOptionsEditor` (traditional mode only).

Injects `IRunningOrderScheduler` and `IBandService`. Subscribes to `OnChange`. Calls `Recalculate` after every mutation before calling `BandService.UpdateRunningOrder`.

### Components (`src/FestivalRider/Components`)

#### `ScheduleGantt.razor`

Parameters:

```csharp
[Parameter] public RunningOrder Order { get; set; } = default!;
[Parameter] public ShowData Show { get; set; } = default!;
[Parameter] public int SelectedSlotIndex { get; set; } = -1;
[Parameter] public EventCallback<int> OnSelectSlot { get; set; }
[Parameter] public EventCallback<(int slotIndex, TimingEventType eventType, DateTime newStart)> OnMoveEvent { get; set; }
```

Renders one horizontal row per band. Each row contains colored bars:
- Orange: soundcheck block (`PreShowEvents` where `EventType == SOUNDCHECK`)
- Red: early soundcheck block (`EarlyChain`)
- Green: on-stage block (`OnStage` → `OnStage + SetLength`)
- Blue: post-show blocks (`PostShowEvents`)
- Yellow: other events

Cross-day times display `+1d`, `+2d` badges. Dragging a bar body shifts the start time (respects `IsPinned`). Dragging a bar edge is NOT supported in v1.

#### `ScheduleBandPanel.razor`

Parameters:

```csharp
[Parameter] public RunningOrderSlot? Slot { get; set; }
[Parameter] public FestivalTimingTemplate? Template { get; set; }
[Parameter] public VenueTimingOptions? VenueOptions { get; set; }
[Parameter] public EventCallback<RunningOrderSlot> OnUpdate { get; set; }
```

Three collapsible sections:

1. **Early Chain** (only visible if `Slot.EarlyChain.Count > 0`): table of `SlotTimingEvent` (Event | Start | Duration | Pinned).
2. **Pre-Show Chain**: table of `PreShowEvents` in reverse order (closest to OnStage first). Special read-only row for `BackstageTime`. Editable fields for `ChangeoverMinutes` and `PreShowLinecheckMinutes` (the canonical properties).
3. **Post-Show Chain**: table of `PostShowEvents` (Event | Start | Duration | Pinned).
4. **Constraints**: `BackstageCurfewTime` datetime input, `HasPersonalBackstageCurfew` checkbox, `CateringSlot` inputs.
5. **Flags**: `UserOverrideFlags` checkboxes (Allow overlap).

#### `TemplateEditor.razor`

Modal for editing `FestivalTimingTemplate`. Three sortable lists: Early Chain, Pre-Show Entries, Post-Show Entries. Each entry shows: drag handle, `TimingEventType` dropdown, custom display name input, duration minutes input, optional toggle. Preset buttons: "Festival Main Stage", "Festival Tent", "Traditional Venue".

#### `VenueOptionsEditor.razor`

Panel for traditional mode: checkboxes for `IncludeGetIn`, `IncludeStageLoadIn`, `IncludeSetupOnStage`, `IncludeSoundcheck`, `IncludePreShowLinecheck`. Number inputs for all default durations.

### Migrators (`src/FestivalRider/Migrators`)

#### `V5ToV6Migrator.cs`

```csharp
using FestivalRider.Models;

namespace FestivalRider.Migrators;

public class V5ToV6Migrator : IStateMigrator
{
    public int FromVersion => 5;
    public int ToVersion => 6;

    public AppState Migrate(AppState state)
    {
        state.SchemaVersion = 6;
        foreach (var show in state.Shows)
        {
            show.Bands ??= new();
            show.RunningOrders ??= new();
            show.Stages ??= new();
            show.StageLinkGroups ??= new();
            show.DefaultScheduleMode = ScheduleMode.Traditional;
            show.DefaultAnchorEvent = TimingEventType.ON_STAGE;
            show.BreakTimeMinutes = 120;
            show.SoundcheckGapMinutes = 0;

            foreach (var ro in show.RunningOrders)
            {
                ro.Slots ??= new();
                ro.VenueOptions = new VenueTimingOptions();
                ro.FestivalTemplate = null;

                var newSlots = new List<RunningOrderSlot>();
                foreach (var oldSlot in ro.Slots)
                {
                    // Legacy slot was a record with positional args
                    // We need to read the old shape from the JSON payload
                    // This migrator assumes the old JSON had: StartTime (TimeOnly), SetLengthMinutes, ChangeoverMinutes, Notes
                    var migrated = MigrateLegacySlot(oldSlot, ro);
                    newSlots.Add(migrated);
                }
                ro.Slots = newSlots;
            }
        }
        return state;
    }

    private RunningOrderSlot MigrateLegacySlot(object oldSlot, RunningOrder order)
    {
        // Pseudocode: actual implementation uses JSON element traversal
        var slot = new RunningOrderSlot
        {
            BandId = ReadBandId(oldSlot),
            StageId = ReadStageId(oldSlot),
            OnStageTime = BaseDateFor(order).Add(ReadStartTime(oldSlot).ToTimeSpan()),
            IsOnStagePinned = true,
            SetLengthMinutes = ReadSetLengthMinutes(oldSlot),
            ChangeoverMinutes = ReadChangeoverMinutes(oldSlot),
            PreShowLinecheckMinutes = 10, // legacy had no linecheck; use default
            SoundcheckOrderIndex = 0, // will be recalculated by scheduler
            Notes = ReadNotes(oldSlot),
            PreShowEvents = new(),
            PostShowEvents = new(),
            EarlyChain = new(),
        };
        return slot;
    }
}
```

> **Note:** The actual migrator will traverse the `JsonElement` directly because the old `RunningOrderSlot` type no longer exists at compile time. The migrator MUST NOT reference the old `RunningOrderSlot` record type. Instead, it reads the raw JSON shape (`startTime`, `setLengthMinutes`, `changeoverMinutes`, `notes`) and constructs new `RunningOrderSlot` instances.

### BundleMigrators (`src/FestivalRider/BundleMigrators`)

#### `V5ToV6BundleMigrator.cs`

Same conversion logic as `V5ToV6Migrator` but applied to bundle manifest JSON before entity decode. The bundle CSV format for running orders gains new columns; the migrator maps old 6-column CSV to new format.

### Localization (`src/FestivalRider/wwwroot/i18n`)

#### Required new keys in `en.json`

```json
{
  "page.runningOrder.modeLabel": "Schedule mode",
  "page.runningOrder.anchorLabel": "Anchor event",
  "page.runningOrder.doorsLabel": "Doors opening",
  "page.runningOrder.firstShowLabel": "First show",
  "page.runningOrder.curfewLabel": "Sound curfew",
  "page.runningOrder.backstageCurfewLabel": "Backstage curfew",
  "page.runningOrder.breakTimeLabel": "Break time (min)",
  "page.runningOrder.soundcheckGapLabel": "Soundcheck gap (min)",
  "page.runningOrder.warning.banner": "{0} schedule warning(s)",
  "page.runningOrder.allowOverlap": "Allow overlap",
  "page.runningOrder.templateEditor.title": "Edit schedule template",
  "page.runningOrder.venueOptions.title": "Venue options",
  "page.runningOrder.gantt.legend.soundcheck": "Soundcheck",
  "page.runningOrder.gantt.legend.onStage": "On stage",
  "page.runningOrder.gantt.legend.early": "Early soundcheck",
  "page.runningOrder.gantt.legend.post": "Post-show",
  "page.runningOrder.panel.earlyChain": "Early soundcheck",
  "page.runningOrder.panel.preShow": "Pre-show",
  "page.runningOrder.panel.postShow": "Post-show",
  "page.runningOrder.panel.backstageTime": "Backstage",
  "page.runningOrder.panel.changeover": "Changeover",
  "page.runningOrder.panel.linecheck": "Linecheck",
  "page.runningOrder.panel.catering": "Catering",
  "page.runningOrder.panel.backstageCurfew": "Backstage curfew",
  "enum.TimingEventType.GET_IN": "Get-in",
  "enum.TimingEventType.LOAD_IN_VENUE": "Venue load-in",
  "enum.TimingEventType.LOAD_IN_STAGE": "Stage load-in",
  "enum.TimingEventType.BACKSTAGE_DROP": "Backstage drop",
  "enum.TimingEventType.CATERING": "Catering",
  "enum.TimingEventType.SETUP_ON_STAGE": "Setup on stage",
  "enum.TimingEventType.SOUNDCHECK": "Soundcheck",
  "enum.TimingEventType.CHANGEOVER": "Changeover",
  "enum.TimingEventType.PRESHOW_LINECHECK": "Pre-show linecheck",
  "enum.TimingEventType.ON_STAGE": "On stage",
  "enum.TimingEventType.LOAD_OUT_STAGING": "Load out to staging",
  "enum.TimingEventType.LOAD_OUT_VENUE": "Load out from venue",
  "enum.TimingEventType.BACKSTAGE_WAIT": "Backstage wait",
  "enum.ScheduleMode.Traditional": "Traditional venue",
  "enum.ScheduleMode.Festival": "Festival",
  "enum.ScheduleWarningType.BreakTimeViolation": "Break time violation",
  "enum.ScheduleWarningType.SoundcheckBlockOverlap": "Soundcheck block overlap",
  "enum.ScheduleWarningType.OnStageOverlap": "On-stage overlap",
  "enum.ScheduleWarningType.BackwardLockConflict": "Backward lock conflict",
  "enum.ScheduleWarningType.BarrierConflict": "Barrier conflict",
  "enum.ScheduleWarningType.CateringOutsideHours": "Catering outside hours",
  "enum.ScheduleWarningType.CurfewViolation": "Curfew violation",
  "enum.ScheduleWarningType.SoundcheckShrunk": "Soundcheck duration shrunk",
  "enum.ScheduleWarningType.SoundcheckOrderOverlap": "Soundcheck order overlap",
  "enum.ScheduleWarningType.UserOverrideOverlap": "Overlap (user allowed)",
  "enum.ScheduleWarningType.EarlySoundcheckAfterOnStage": "Early soundcheck after on-stage",
  "enum.ScheduleWarningType.ConstraintViolation": "Constraint violation",
  "enum.ScheduleWarningType.FirstShowTimeMissing": "First show time missing; using default"
}
```

`fr-fr.json` MUST have 1:1 parity. `LocalizationKeys.cs` MUST have a constant for every key.

### CSV format changes

Running-order CSV header becomes:

```
ShowId,Stage,OnStageTime,SetLengthMinutes,ChangeoverMinutes,PreShowLinecheckMinutes,SoundcheckOrderIndex,BackstageLeadMinutes,BackstageTime,BackstageCurfewTime,Notes
```

For backward compatibility during bundle import, `V5ToV6BundleMigrator` maps the old 6-column format (`ShowId,Stage,StartTime,BandName,SetLengthMinutes,ChangeoverMinutes`) to the new format by:
1. Dropping `BandName` (resolved via `BandId` lookup)
2. Mapping `StartTime` → `OnStageTime`
3. Setting `ChangeoverMinutes` from old column
4. Defaulting all new columns (`PreShowLinecheckMinutes=10`, `SoundcheckOrderIndex=0`, etc.)

Byte-stability test in `ExportServiceTests` MUST be updated.

### Program.cs

Add registration:

```csharp
builder.Services.AddScoped<IRunningOrderScheduler, RunningOrderScheduler>();
```

## Task order

1. **Add new model files** (`TimingEventType.cs`, `ScheduleMode.cs`, `StageLinkConstraint.cs`, `StageLinkGroup.cs`, `TimingChainEntry.cs`, `FestivalTimingTemplate.cs`, `VenueTimingOptions.cs`, `SlotTimingEvent.cs`, `BandScheduleFlags.cs`, `UserOverrideFlags.cs`, `ScheduleWarningType.cs`, `ScheduleWarning.cs`, `ScheduleResult.cs`). App compiles.
2. **Update `RunningOrder.cs`** with new properties. Temporarily keep old `Slots` type compatible by NOT changing `RunningOrderSlot` yet. App compiles.
3. **Replace `RunningOrderSlot`** with the new mutable `class`. Update ALL consumers in the same commit:
   - `RunningOrderSlotRow.razor` — rewrite to display `OnStageTime`, `SetLengthMinutes`, `ChangeoverMinutes` (legacy UI for compatibility)
   - `RunningOrderV2.razor` — update slot construction, remove `new RunningOrderSlot(...)` positional syntax
   - `ExportService.cs` — update CSV read/write for new columns
   - `BundleService.cs` — update slot deserialization
   - `StagePrintStrategy.cs` and `RolePrintStrategy.cs` — read `OnStageTime` instead of `StartTime`
   - All tests. App compiles, all tests pass.
4. **Write `IRunningOrderScheduler` and `RunningOrderScheduler`** with `Traditional` mode algorithm. Add comprehensive unit tests for:
   - Forward on-stage cascade
   - Backward soundcheck packing from deadline
   - `SoundcheckOrderIndex` reordering
   - Barrier conflict detection
   - Break time validation
   - Curfew checks. App compiles, tests pass.
5. **Write `V5ToV6Migrator` and `V5ToV6BundleMigrator`**. Add migration tests covering:
   - Legacy slot with all fields
   - Legacy slot with missing notes
   - Empty slots list
   - Show with no stages. Bump `AppState.SchemaVersion` to 6. App compiles, tests pass.
6. **Add localization keys** for all new UI text. Update `LocalizationKeys.cs`. Update `LocalizationCatalogTests` to cover new enum keys. App compiles, tests pass.
7. **Write `FestivalTimingTemplate` algorithm** in `RunningOrderScheduler`. Add unit tests for:
   - Template chain growth from `ON_STAGE` anchor
   - Template chain growth from `SOUNDCHECK` anchor
   - Early chain validation
   - Post-show chain growth
   - Same-stage overlap checks. App compiles, tests pass.
8. **Create UI components**: `ScheduleGantt.razor`, `ScheduleBandPanel.razor`, `TemplateEditor.razor`, `VenueOptionsEditor.razor`. Wire into `RunningOrderV2.razor`. App compiles, runnable.
9. **Update print strategies** to consume new `RunningOrderSlot` fields. No functional change to printed output layout; just field mapping. App compiles.
10. **Update CSV writer/reader** for new running-order columns. Update `ExportServiceTests` for byte stability. App compiles, tests pass.
11. **Add preset templates** to `TemplateEditor.razor`:
    - "Traditional Venue": PreShow = [Linecheck, Changeover, Setup, StageLoadIn]; PostShow = [LoadOut]
    - "Festival Main Stage": PreShow = [Linecheck, Changeover, Setup, StageLoadIn]; PostShow = [LoadOut, LoadOutStaging, BackstageWait]
    - "Festival Tent": PreShow = [Linecheck, Changeover]; PostShow = [LoadOut, LoadOutVenue]. App compiles, runnable.
12. **Full integration test**: create show, add bands, switch modes, add slots, verify Gantt and warnings. Commit.

## Implementation cadence

- **Wave 1 — Models and legacy compatibility** (tasks 1–3): All new model files exist. `RunningOrderSlot` is a mutable class. All existing consumers compile. Tests pass. Legacy CSV still imports correctly.
- **Wave 2 — Traditional scheduler and migration** (tasks 4–6): Traditional mode algorithm works end-to-end. Adding a band computes soundcheck times backward from the deadline. Warnings appear for break-time violations. v5 bundles migrate cleanly to v6.
- **Wave 3 — Festival scheduler and UI** (tasks 7–9): Festival mode algorithm works. Template editor allows reordering events. Gantt renders color-coded bars. Side panel edits durations and pins.
- **Wave 4 — CSV, print, presets, polish** (tasks 10–12): Running-order CSV exports with new columns. Print strategies map to new fields. Preset templates populate with one click. Full integration test passes.

## Out of scope

- Stage linking UI and enforcement (`StageLinkGroups` schema is in place; UI enforcement deferred to successor plan 021).
- Auto-packing soundchecks into gaps between on-stage times in festival mode (user-placed only for v1).
- Drag-to-resize duration on the Gantt (v1 supports drag-to-move only; duration edits via side panel).
- Live cross-tab sync of running order edits.
- PDF export of the Gantt chart (print strategies continue to emit text tables).
- Catering `TimeSlot` auto-reanchoring when `DateOfOpening` changes.
- Orphaned `StageLinkGroup.StageIds` cleanup when a stage is deleted.
- Multi-select bulk operations on the Gantt (e.g. move 3 bands at once).

## Risks & migrations

- **Risk: legacy `RunningOrderSlot` is an immutable `record`** — the new model is a mutable `class`. The v5→v6 migrator must convert every legacy record into a new `class` instance with correct defaults. The migrator CANNOT reference the old `RunningOrderSlot` type at compile time (it no longer exists). It MUST read raw JSON properties (`startTime`, `setLengthMinutes`, `changeoverMinutes`, `notes`) and construct new instances manually. Mitigation: thorough migrator tests covering empty slots, missing notes, and pinned/unpinned semantics.
- **Risk: CSV format change breaks external tools** — new columns (`OnStageTime`, `PreShowLinecheckMinutes`, `SoundcheckOrderIndex`, etc.) are added. The old 6-column format is no longer emitted. Mitigation: `ExportServiceTests` validates byte stability of the NEW format. `V5ToV6BundleMigrator` handles import of old-format CSVs. Document the new format in the plan.
- **Risk: `DateTime` conversion breaks existing `TimeOnly` consumers** — `ExportService`, print strategies, and `RunningOrderSlotRow` read `TimeOnly`. Mitigation: convert at the boundary (`DateTime.TimeOfDay` for same-day, display `+1d` for cross-day). All consumers are updated in the same commit as the model change (task 3).
- **Risk: dual-mode logic complexity in one scheduler** — `RunningOrderScheduler` carries two algorithms. Mitigation: private methods `ComputeTraditionalTimeline` and `ComputeFestivalTimeline` with shared helper methods (`CheckOverlap`, `CheckCurfew`, `ResolveEffectiveGlobals`). Unit tests cover both paths independently.
- **Risk: `BandService.UpdateShow` wipes schedule configuration** — if the method only copies the old scalar set (`Name`, `Address`, `DateOfOpening`, `ShowDayCount`, `Stages`), the new fields (`FirstShowTime`, `BreakTimeMinutes`, etc.) are lost on every show edit. Mitigation: explicitly list all scalar fields in the update method (see code in File-by-file scope).
- **Risk: missing localization keys block merge** — `LocalizationCatalogTests` enforces parity. If a new enum value or UI label is added without its localization key, the build fails. Mitigation: every task that adds a user-facing string MUST also add the key to `en.json`, `fr-fr.json`, and `LocalizationKeys.cs` in the same commit.
