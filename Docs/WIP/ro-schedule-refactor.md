Here is the **final revised architecture**, incorporating all corrections and additions.

---

## 1. Shared value objects

```csharp
// Reusable time range. Start=opening, End=closing. End=null means point-in-time.
public class TimeSlot
{
    public TimeOnly Start { get; set; }
    public TimeOnly? End { get; set; }
}
```

---

## 2. Show-level globals (`ShowData`)

```csharp
public class ShowData
{
    // --- existing fields: Id, Name, Address, DateOfOpening, ShowDayCount, Stages, Bands, RunningOrders ---
    
    // Global schedule anchors (nullable = not set)
    public TimeOnly? TechnicalGetInTime { get; set; }
    public TimeOnly? DoorsOpeningTime { get; set; }
    public TimeOnly? SoundCurfewTime { get; set; }
    public TimeOnly? BackstageCurfewTime { get; set; }
    
    // Catering windows
    public TimeSlot? BreakfastHours { get; set; }
    public TimeSlot? LunchHours { get; set; }
    public TimeSlot? DinnerHours { get; set; }
    
    // Minimum gap between last soundcheck end and earlier of (doors, first on-stage)
    public int BreakTimeMinutes { get; set; } = 120;
    
    // Standard durations for bands added to any day in this show
    public BandTimingDefaults DefaultTimings { get; set; } = new();
    
    // Gap between consecutive soundcheck blocks on the same stage
    public int SoundcheckGapMinutes { get; set; } = 0;
    
    // Stage linking: groups of stages that share overlap constraints
    public List<StageLinkGroup> StageLinkGroups { get; set; } = new();
}

public class StageLinkGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<int> StageIds { get; set; } = new(); // references Stage.Id
    public StageLinkConstraint Constraint { get; set; } = StageLinkConstraint.All;
}

public enum StageLinkConstraint
{
    All,       // soundcheck↔soundcheck, soundcheck↔onstage, onstage↔onstage
    OnStageOnly // only on-stage overlaps are checked across linked stages
}
```

> **Stage linking:** Schema and model are in place now. UI and scheduler enforcement may ship in a successor plan if scope grows too large.

---

## 3. Per-day overrides and defaults (`RunningOrder`)

```csharp
public class RunningOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShowId { get; set; }
    public int ShowDayNumber { get; set; } = 1;
    
    // Per-day overrides (null = inherit from ShowData)
    public TimeOnly? TechnicalGetInTimeOverride { get; set; }
    public TimeOnly? DoorsOpeningTimeOverride { get; set; }
    public TimeOnly? SoundCurfewTimeOverride { get; set; }
    public TimeOnly? BackstageCurfewTimeOverride { get; set; }
    public TimeSlot? BreakfastHoursOverride { get; set; }
    public TimeSlot? LunchHoursOverride { get; set; }
    public TimeSlot? DinnerHoursOverride { get; set; }
    public int? BreakTimeMinutesOverride { get; set; }
    public int? SoundcheckGapMinutesOverride { get; set; }
    
    // Standard durations for bands added to THIS day (null fields fall back to ShowData.DefaultTimings)
    public BandTimingDefaults DefaultTimings { get; set; } = new();
    
    // Which timings are auto-generated when a band is added to this RO
    public AutoTimingFlags AutoTimings { get; set; } = AutoTimingFlags.All;
    
    public List<RunningOrderSlot> Slots { get; set; } = new();
}
```

`BandTimingDefaults` — standard durations:

```csharp
public class BandTimingDefaults
{
    // Pre-block arrival
    public int? GetInMinutes { get; set; }
    
    // Soundcheck block (band timeslot: StageLoadIn → SoundcheckEnd)
    public int? VenueLoadInMinutes { get; set; } // optional split; null = same as stage load-in
    public int? StageLoadInMinutes { get; set; }
    public int? SetupOnStageMinutes { get; set; }
    public int? SoundcheckMinutes { get; set; }
    
    // On-stage block
    public int? BackstageLeadMinutes { get; set; }
    public int? PreShowLinecheckMinutes { get; set; }
    public int? SetLengthMinutes { get; set; }
    public int? ChangeoverMinutes { get; set; }
}
```

`AutoTimingFlags`:

```csharp
[Flags]
public enum AutoTimingFlags
{
    None = 0,
    GetIn = 1,
    VenueLoadIn = 2,
    StageLoadIn = 4,
    SetupOnStage = 8,
    Soundcheck = 16,
    Catering = 32,
    Backstage = 64,
    PreShowLinecheck = 128,
    OnStage = 256,
    Changeover = 512,
    All = GetIn | VenueLoadIn | StageLoadIn | SetupOnStage | Soundcheck
        | Catering | Backstage | PreShowLinecheck | OnStage | Changeover
}
```

---

## 4. Per-band slot (`RunningOrderSlot`) — replaces the `record`

Per the project's rules, UI-mutated entities must be `class { get; set; }`.

```csharp
public class RunningOrderSlot
{
    public Guid BandId { get; set; }
    public int StageId { get; set; }
    
    // === Soundcheck block (band timeslot) ===
    // Contiguous: [StageLoadInTime ... SoundcheckEndTime]
    // Constraint: may NOT overlap another band's on-stage time on the same stage.
    public TimeOnly? VenueLoadInTime { get; set; }   // optional split; unconstrained
    public TimeOnly? StageLoadInTime { get; set; }   // start of constrained block
    public TimeOnly? SetupOnStageTime { get; set; }
    public TimeOnly? SoundcheckStartTime { get; set; }
    public TimeOnly? SoundcheckEndTime { get; set; }
    
    // === Pre-block arrival ===
    public TimeOnly? GetInTime { get; set; }
    
    // === On-stage block ===
    public TimeOnly? BackstageTime { get; set; }
    public TimeOnly? PreShowLinecheckTime { get; set; }
    public TimeOnly? OnStageTime { get; set; }
    
    // === Durations (null = use RunningOrder.DefaultTimings) ===
    public int? GetInMinutes { get; set; }
    public int? VenueLoadInMinutes { get; set; }
    public int? StageLoadInMinutes { get; set; }
    public int? SetupOnStageMinutes { get; set; }
    public int? SoundcheckMinutes { get; set; }
    public int? BackstageLeadMinutes { get; set; }
    public int? PreShowLinecheckMinutes { get; set; }
    public int? SetLengthMinutes { get; set; }
    public int? ChangeoverMinutes { get; set; }
    
    // === Lock / anchor flags ===
    // Every timing and every duration can be locked independently.
    public SlotPinFlags PinFlags { get; set; }
    
    // === Soundcheck order ===
    // Controls the packing order of soundchecks. Default is reverse of playing order.
    // 0 = first soundcheck of the day, 1 = second, etc.
    // The UI can override by reordering; the scheduler packs soundchecks in this order.
    public int SoundcheckOrderIndex { get; set; }
    
    // === Catering ===
    public TimeSlot? CateringSlot { get; set; }
    
    public string? Notes { get; set; }
}
```

`SlotPinFlags`:

```csharp
[Flags]
public enum SlotPinFlags
{
    None = 0,
    GetIn = 1,
    VenueLoadIn = 2,
    StageLoadIn = 4,
    SetupOnStage = 8,
    SoundcheckStart = 16,
    SoundcheckEnd = 32,
    Catering = 64,
    Backstage = 128,
    PreShowLinecheck = 256,
    OnStage = 512,
    // Duration pins
    GetInDuration = 1_024,
    VenueLoadInDuration = 2_048,
    StageLoadInDuration = 4_096,
    SetupOnStageDuration = 8_192,
    SoundcheckDuration = 16_384,
    BackstageLeadDuration = 32_768,
    PreShowLinecheckDuration = 65_536,
    SetLengthDuration = 131_072,
    ChangeoverDuration = 262_144,
}
```

---

## 5. Service layer

```csharp
public interface IRunningOrderScheduler
{
    // Full recalculate. Mutates the RO in-place. Returns warnings.
    ScheduleResult Recalculate(RunningOrder order, ShowData show);
    
    // Add a band. Placement is either insert index in playing order, or pinned on-stage time.
    ScheduleResult AddBand(RunningOrder order, Guid bandId, BandPlacement placement, ShowData show);
    
    // Remove a band and recalc.
    ScheduleResult RemoveBand(RunningOrder order, int slotIndex, ShowData show);
    
    // Move a band in the on-stage playing order (per stage).
    ScheduleResult MoveBand(RunningOrder order, int fromIndex, int toIndex, ShowData show);
    
    // Reorder soundchecks independently of playing order.
    ScheduleResult SetSoundcheckOrder(RunningOrder order, int slotIndex, int newSoundcheckIndex, ShowData show);
    
    // Validate without mutating.
    List<ScheduleWarning> Validate(RunningOrder order, ShowData show);
}

public class BandPlacement
{
    public Guid BandId { get; set; }
    public int StageId { get; set; }
    public int? InsertAtIndex { get; set; }
    public TimeOnly? PinnedOnStageTime { get; set; }
}
```

`ScheduleResult` and `ScheduleWarning`:

```csharp
public class ScheduleResult
{
    public bool Success { get; set; }
    public List<ScheduleWarning> Warnings { get; set; } = new();
}

public enum ScheduleWarningType
{
    BreakTimeViolation,      // last soundcheck end too close to doors/first on-stage
    SoundcheckBlockOverlap,  // band timeslot overlaps another band's on-stage on same stage
    OnStageOverlap,          // two bands on same stage with overlapping sets
    BackwardLockConflict,    // pinned backward timing forces on-stage later than cascade
    BarrierConflict,         // cascade hit a locked barrier and stopped
    CateringOutsideHours,    // band catering outside global meal windows
    CurfewViolation,         // band exceeds sound or backstage curfew
    SoundcheckShrunk,        // pinned start forced duration below default
    SoundcheckOrderOverlap,  // two soundcheck blocks overlap (forbidden by default)
    UserOverrideOverlap,     // overlap exists but user explicitly allowed it
}

public class ScheduleWarning
{
    public ScheduleWarningType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? SlotIndex { get; set; } // null = global warning
    public int? RelatedSlotIndex { get; set; } // the other band involved
}
```

---

## 6. Scheduler algorithm (detailed)

### Step A — Resolve effective globals

For each timing, pick `RunningOrder.Override` if not null, else `ShowData.Default`. This yields the day's concrete values:
`EffectiveDoors`, `EffectiveCurfew`, `EffectiveBreakTime`, `EffectiveSoundcheckGap`, etc.

### Step B — On-stage timeline (forward cascade, **per stage**)

Group `Slots` by `StageId`. For each stage group:

1. **Sort by list index** (the user's playing order).
2. **First band on the stage**:
   - `OnStageTime` = `EffectiveDoors` (if set) or 12:00 fallback, unless `PinFlags.OnStage` is set.
3. **Subsequent bands**:
   - `OnStageTime` = `previous.OnStageTime + previous.SetLengthMinutes + previous.ChangeoverMinutes`, unless pinned.
   - If pinned and computed cascade is earlier: cascade stops at the previous locked barrier. Bands between barrier and pinned slot keep existing times (or stay unassigned if newly inserted). Emit `BarrierConflict`.
   - If pinned and computed cascade is later: band moves later; subsequent bands recalc from there.

4. **Backward events within the on-stage block**:
   - `PreShowLinecheckTime` = `OnStageTime - PreShowLinecheckMinutes` (or pinned).
   - `BackstageTime` = `min(OnStageTime - BackstageLeadMinutes, PreShowLinecheckTime)` (or pinned).
     - If linecheck is at 19:40 and backstage lead normally puts you at 19:50, you are backstage at 19:40 because you must be present for linecheck.

### Step C — Soundcheck timeline (backward packing, **per stage**)

Sort `Slots` within each stage by `SoundcheckOrderIndex` (default = reverse of playing order).

1. Determine the **soundcheck deadline** for this stage:
   - `deadline = min(EffectiveDoors, FirstOnStageTimeOnThisStage) - EffectiveBreakTime`
   - If `EffectiveDoors` is null, use `FirstOnStageTimeOnThisStage` alone.

2. **Pack backward from deadline**:
   - Last soundcheck band: `SoundcheckEndTime` = `deadline` (or pinned).
   - For each preceding band in soundcheck order:
     - `SoundcheckEndTime` = next band's `StageLoadInTime - EffectiveSoundcheckGap` (or pinned).
     - `SoundcheckStartTime` = `SoundcheckEndTime - SoundcheckMinutes` (or pinned).
       - If `SoundcheckStartTime` is pinned and the available window is smaller than the default duration, **shrink** the actual duration to fit. Emit `SoundcheckShrunk`.
     - `SetupOnStageTime` = `SoundcheckStartTime - SetupOnStageMinutes` (or pinned).
     - `StageLoadInTime` = `SetupOnStageTime - StageLoadInMinutes` (or pinned).
     - `GetInTime` = `StageLoadInTime - GetInMinutes` (or pinned).

3. **Venue load-in split**:
   - If `VenueLoadInMinutes` is set (or `VenueLoadInTime` is pinned), render it independently. It is unconstrained.
   - If null, do not render a separate venue load-in row.

### Step D — Overlap checks

**Per stage** (always):
- No two soundcheck blocks `[StageLoadIn ... SoundcheckEnd]` may overlap.
- No soundcheck block may overlap with another band's on-stage time on the **same stage**.
- No two on-stage times may overlap on the same stage.

**Linked stages** (if `StageLinkGroups` populated):
- For each `StageLinkGroup` with `Constraint = All`: all the above checks also apply across every stage in the group.
- For `Constraint = OnStageOnly`: only on-stage overlap is checked across linked stages.

**Forbidden by default; user override:**
- If any overlap is detected, emit the appropriate warning.
- The UI shows a prominent warning badge. The user can click "Allow overlap" which sets a per-slot `UserOverrideFlags` (new field on `RunningOrderSlot`). Once overridden, the overlap warning becomes `UserOverrideOverlap` (informational, not blocking).

### Step E — Global constraints

- `max(all SoundcheckEndTimes across all stages) + EffectiveBreakTime ≤ min(EffectiveDoors, FirstOnStageTime)`. If violated: `BreakTimeViolation`.
- Last band's `OnStageTime + SetLengthMinutes ≤ EffectiveSoundCurfew` (if set).
- `BackstageCurfewTime` checked against latest `BackstageTime`.
- Each `CateringSlot` checked against the day's active meal windows. Warn if outside.

---

## 7. UI layer

- **`ScheduleGantt.razor`** — Horizontal timeline. Two bars per band:
  - **Orange**: soundcheck block (`StageLoadIn` → `SoundcheckEnd`)
  - **Green**: on-stage block (`Backstage` → `OnStage` + `SetLength`)
  - Click-to-select. Drag bar body to shift time (respects pins). Drag bar edge to resize duration (only if duration unlocked). Emits `EventCallback<SlotEditRequest>`.

- **`ScheduleBandPanel.razor`** — Side panel with selected band's full timing chain as editable table. Columns: Event | Time | Duration | Pinned [toggle]. Special rows:
  - "Split load-in" toggle reveals `VenueLoadInTime`
  - "Soundcheck order" shows current index; drag handle to reorder soundchecks independently of playing order
  - "Allow overlap" button appears when warnings exist

- **`RunningOrderV2.razor`** (or renamed to `Schedule.razor`) — Orchestrator. Injects `IRunningOrderScheduler` and `IBandService`. Shows:
  - Top toolbar: global settings (doors, curfew, catering, BreakTime, SoundcheckGap)
  - Warning toasts/badges
  - Both components

---

## 8. Migration impact

- `AppState.SchemaVersion` bumps **5 → 6**.
- `V5ToV6Migrator` and `V5ToV6BundleMigrator`:
  - Convert legacy `RunningOrderSlot` records:
    - `StartTime` → `OnStageTime` with `PinFlags.OnStage` set.
    - `SetLengthMinutes` / `ChangeoverMinutes` → carried over as durations.
    - All new fields default to `null` / unpinned.
    - `SoundcheckOrderIndex` defaults to reverse of playing order.
    - `StageLinkGroups` defaults to empty.
- Running-order CSV gains new columns. Per the byte-stability rule, writer, reader, and `ExportServiceTests` are updated together.

---

## Summary of what was corrected from the previous draft

| Issue | Fix |
|---|---|
| Missing `TechnicalGetInTime` from per-day overrides | Added `TechnicalGetInTimeOverride` to `RunningOrder` |
| No soundcheck order override | Added `SoundcheckOrderIndex` to each `RunningOrderSlot`; scheduler packs by this index |
| No soundcheck gap | Added `SoundcheckGapMinutes` to `ShowData` and override to `RunningOrder` |
| No stage linking | Added `StageLinkGroup` / `StageLinkConstraint` to `ShowData` (deferrable impl) |
| No user-override mechanism for forbidden overlaps | Added `UserOverrideFlags` concept on `RunningOrderSlot` + `UserOverrideOverlap` warning |
| Catering not handling both point-in-time and range | `TimeSlot.End` is nullable; UI renders accordingly |
| Soundcheck block terminology | Clarified as contiguous `[StageLoadIn ... SoundcheckEnd]` with constraints |

---

Is there anything in this architecture you'd like to adjust before I call `exit_plan_mode`?