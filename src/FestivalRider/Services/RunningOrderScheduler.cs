using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

// Wave-1 scope: Traditional pipeline only. Festival pipeline returns Success with no warnings and
// leaves the graph unchanged so callers can plumb through until wave 3 wires up the festival
// algorithm. Tests covering the festival branch live with that future work.
public class RunningOrderScheduler : IRunningOrderScheduler
{
    private readonly ILogger<RunningOrderScheduler> _logger;

    public RunningOrderScheduler(ILogger<RunningOrderScheduler> logger)
    {
        _logger = logger;
    }

    // -------- public surface --------

    public ScheduleResult Recalculate(RunningOrder order, ShowData show)
    {
        var result = new ScheduleResult();
        if (order is null || show is null) return result;
        var ctx = new Ctx(order, show);
        if (ctx.Mode == ScheduleMode.Traditional)
            ComputeTraditionalTimeline(ctx, result);
        return result;
    }

    public ScheduleResult AddSlot(RunningOrder order, BandPlacement placement, ShowData show)
    {
        if (order is null || show is null || placement is null) return new ScheduleResult();
        var ctx = new Ctx(order, show);
        var slot = new RunningOrderSlot
        {
            Id = Guid.NewGuid(),
            BandId = placement.BandId,
            StageId = placement.StageId,
            OnStageTime = placement.PinnedOnStageTime,
            IsOnStagePinned = placement.PinnedOnStageTime.HasValue,
            SoundcheckOrderIndex = order.Slots.Count,
        };
        var insertAt = placement.InsertAtIndex is int idx
            ? Math.Clamp(idx, 0, order.Slots.Count)
            : order.Slots.Count;
        order.Slots.Insert(insertAt, slot);
        SeedSlotEvents(slot, ctx);
        return Recalculate(order, show);
    }

    public ScheduleResult RemoveSlot(RunningOrder order, Guid slotId, ShowData show)
    {
        var result = new ScheduleResult();
        if (order is null || show is null) return result;
        var index = order.Slots.FindIndex(s => s.Id == slotId);
        if (index < 0)
        {
            result.Warnings.Add(new ScheduleWarning
            {
                Type = ScheduleWarningType.ConstraintViolation,
                Message = $"Slot {slotId} not found.",
                SlotId = slotId,
            });
            return result;
        }
        order.Slots.RemoveAt(index);
        return Recalculate(order, show);
    }

    public ScheduleResult MoveSlot(RunningOrder order, Guid slotId, int newIndex, ShowData show)
    {
        if (order is null || show is null) return new ScheduleResult();
        var currentIndex = order.Slots.FindIndex(s => s.Id == slotId);
        if (currentIndex < 0)
        {
            return new ScheduleResult
            {
                Warnings = { new ScheduleWarning
                {
                    Type = ScheduleWarningType.ConstraintViolation,
                    Message = $"Slot {slotId} not found.",
                    SlotId = slotId,
                } }
            };
        }
        var clamped = Math.Clamp(newIndex, 0, order.Slots.Count - 1);
        var slot = order.Slots[currentIndex];
        order.Slots.RemoveAt(currentIndex);
        order.Slots.Insert(clamped, slot);
        return Recalculate(order, show);
    }

    public ScheduleResult SetSoundcheckOrder(RunningOrder order, Guid slotId, int newSoundcheckIndex, ShowData show)
    {
        var slot = order?.Slots.FirstOrDefault(s => s.Id == slotId);
        if (slot is null || order is null || show is null)
        {
            return new ScheduleResult
            {
                Warnings = { new ScheduleWarning
                {
                    Type = ScheduleWarningType.ConstraintViolation,
                    Message = $"Slot {slotId} not found.",
                    SlotId = slotId,
                } }
            };
        }
        slot.SoundcheckOrderIndex = newSoundcheckIndex;
        return Recalculate(order, show);
    }

    public List<ScheduleWarning> Validate(RunningOrder order, ShowData show)
    {
        // Validate runs a fresh pipeline over a logically-copied graph so the caller's graph
        // stays byte-identical (idempotency contract).
        if (order is null || show is null) return new List<ScheduleWarning>();
        var snapshot = order.Slots.Select(CloneSlot).ToList();
        var result = Recalculate(order, show);
        // Restore snapshot to honour the read-only contract.
        for (int i = 0; i < order.Slots.Count && i < snapshot.Count; i++)
            ReplaceFromSnapshot(order.Slots[i], snapshot[i]);
        return result.Warnings;
    }

    // -------- traditional pipeline --------

    private void ComputeTraditionalTimeline(Ctx ctx, ScheduleResult result)
    {
        var options = ctx.EffectiveVenueOptions;
        bool firstShowMissingEmitted = false;
        DateTime EffectiveFirstShow()
        {
            if (ctx.EffectiveFirstShow is DateTime dt) return dt;
            if (!firstShowMissingEmitted)
            {
                result.Warnings.Add(new ScheduleWarning
                {
                    Type = ScheduleWarningType.FirstShowTimeMissing,
                    Message = "First show time missing; defaulting to base date + 20h.",
                });
                firstShowMissingEmitted = true;
            }
            return ctx.BaseDate.AddHours(20);
        }

        // Defensive stage handling.
        foreach (var slot in ctx.Order.Slots)
        {
            if (ctx.Show.Stages.Count > 0 && ctx.Show.Stages.All(s => s.Id != slot.StageId))
            {
                result.Warnings.Add(new ScheduleWarning
                {
                    Type = ScheduleWarningType.ConstraintViolation,
                    Message = $"Slot references unknown stage {slot.StageId}.",
                    SlotId = slot.Id,
                });
            }
        }

        // Forward on-stage cascade per stage, honouring pins.
        var stageGroups = ctx.Order.Slots
            .Where(s => ctx.Show.Stages.Count == 0 || ctx.Show.Stages.Any(st => st.Id == s.StageId))
            .GroupBy(s => s.StageId);
        foreach (var stage in stageGroups)
        {
            var ordered = stage.OrderBy(s => ctx.Order.Slots.IndexOf(s)).ToList();
            DateTime? cursor = null;
            foreach (var slot in ordered)
            {
                var setLen = slot.SetLengthMinutes ?? options.DefaultSetLengthMinutes;
                var changeover = ChangeoverMinutes(slot, options);
                if (slot.IsOnStagePinned && slot.OnStageTime.HasValue)
                {
                    if (cursor.HasValue && slot.OnStageTime.Value < cursor.Value)
                    {
                        result.Warnings.Add(new ScheduleWarning
                        {
                            Type = ScheduleWarningType.BarrierConflict,
                            Message = $"Pinned on-stage time precedes prior slot on stage {slot.StageId}.",
                            SlotId = slot.Id,
                        });
                    }
                    cursor = slot.OnStageTime.Value.AddMinutes(setLen + changeover);
                }
                else
                {
                    if (cursor.HasValue)
                    {
                        slot.OnStageTime = cursor.Value;
                        cursor = cursor.Value.AddMinutes(setLen + changeover);
                    }
                    else
                    {
                        // First (unpinned) slot on stage: anchor at effective first-show time.
                        var anchor = EffectiveFirstShow();
                        slot.OnStageTime = anchor;
                        cursor = anchor.AddMinutes(setLen + changeover);
                    }
                }
            }

            // Same-stage on-stage overlap detection.
            for (int i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var curr = ordered[i];
                if (prev.OnStageTime is DateTime pt && curr.OnStageTime is DateTime ct)
                {
                    var prevEnd = pt.AddMinutes(prev.SetLengthMinutes ?? options.DefaultSetLengthMinutes);
                    if (curr.OverrideFlags.HasFlag(UserOverrideFlags.AllowOnStageOverlap))
                    {
                        if (ct < prevEnd)
                        {
                            result.Warnings.Add(new ScheduleWarning
                            {
                                Type = ScheduleWarningType.UserOverrideOverlap,
                                Message = $"On-stage overlap allowed by user on stage {curr.StageId}.",
                                SlotId = curr.Id,
                                RelatedSlotId = prev.Id,
                            });
                        }
                    }
                    else if (ct < prevEnd)
                    {
                        result.Warnings.Add(new ScheduleWarning
                        {
                            Type = ScheduleWarningType.OnStageOverlap,
                            Message = $"On-stage overlap on stage {curr.StageId}.",
                            SlotId = curr.Id,
                            RelatedSlotId = prev.Id,
                        });
                    }
                }
            }
        }

        // Backward soundcheck pack per stage.
        var doors = ctx.EffectiveDoors;
        var first = ctx.EffectiveFirstShow ?? ctx.BaseDate.AddHours(20);
        var packAnchor = doors is DateTime d ? (d < first ? d : first) : first;
        packAnchor = packAnchor.AddMinutes(-ctx.EffectiveBreakTime);

        foreach (var stage in stageGroups)
        {
            var ordered = stage.OrderBy(s => s.SoundcheckOrderIndex).ToList();
            DateTime cursor = packAnchor;
            foreach (var slot in ordered)
            {
                var sc = slot.PreShowEvents.FirstOrDefault(e => e.EventType == TimingEventType.SOUNDCHECK);
                if (sc is null)
                    continue;
                var dur = sc.DurationMinutes ?? options.DefaultSoundcheckMinutes;
                if (!sc.IsPinned)
                {
                    sc.StartTime = cursor.AddMinutes(-dur);
                }
                cursor = (sc.StartTime ?? cursor).AddMinutes(-ctx.EffectiveSoundcheckGap);

                if (ctx.EffectiveTechnicalGetIn is DateTime tgi && sc.StartTime is DateTime st && st < tgi)
                {
                    result.Warnings.Add(new ScheduleWarning
                    {
                        Type = ScheduleWarningType.SoundcheckShrunk,
                        Message = $"Soundcheck start {st:HH:mm} precedes technical get-in {tgi:HH:mm}.",
                        SlotId = slot.Id,
                    });
                }
            }
        }

        // Derive BackstageTime when not pinned.
        foreach (var slot in ctx.Order.Slots)
        {
            if (slot.IsBackstageTimePinned) continue;
            var lead = slot.BackstageLeadMinutes ?? options.DefaultBackstageLeadMinutes;
            var candidates = new List<DateTime>();
            var changeover = slot.PreShowEvents.FirstOrDefault(e => e.EventType == TimingEventType.CHANGEOVER);
            if (changeover?.StartTime is DateTime c) candidates.Add(c);
            var linecheck = slot.PreShowEvents.FirstOrDefault(e => e.EventType == TimingEventType.PRESHOW_LINECHECK);
            if (linecheck?.StartTime is DateTime l) candidates.Add(l);
            if (slot.OnStageTime is DateTime o) candidates.Add(o);
            if (candidates.Count == 0) continue;
            slot.BackstageTime = candidates.Min().AddMinutes(-lead);
        }

        // Sound curfew + backstage curfew checks.
        foreach (var slot in ctx.Order.Slots)
        {
            if (ctx.EffectiveSoundCurfew is DateTime sc && slot.OnStageTime is DateTime onStage)
            {
                var end = onStage.AddMinutes(slot.SetLengthMinutes ?? options.DefaultSetLengthMinutes);
                if (end > sc)
                {
                    result.Warnings.Add(new ScheduleWarning
                    {
                        Type = ScheduleWarningType.CurfewViolation,
                        Message = $"Set end {end:HH:mm} exceeds sound curfew {sc:HH:mm}.",
                        SlotId = slot.Id,
                    });
                }
            }
            var effectiveBackstageCurfew = slot.Flags.HasFlag(BandScheduleFlags.HasPersonalBackstageCurfew)
                ? slot.BackstageCurfewTime
                : ctx.EffectiveBackstageCurfew;
            if (effectiveBackstageCurfew is DateTime bc && slot.OnStageTime is DateTime os)
            {
                var setEnd = os.AddMinutes(slot.SetLengthMinutes ?? options.DefaultSetLengthMinutes);
                if (setEnd > bc)
                {
                    result.Warnings.Add(new ScheduleWarning
                    {
                        Type = ScheduleWarningType.CurfewViolation,
                        Message = $"Set end {setEnd:HH:mm} exceeds backstage curfew {bc:HH:mm}.",
                        SlotId = slot.Id,
                    });
                }
            }
        }

        // Catering window check.
        foreach (var slot in ctx.Order.Slots)
        {
            if (slot.CateringSlot is null) continue;
            var windows = new List<TimeSlot?>
            {
                ctx.EffectiveBreakfast,
                ctx.EffectiveLunch,
                ctx.EffectiveDinner,
            }.Where(w => w is not null).Cast<TimeSlot>().ToList();
            if (windows.Count == 0) continue; // No active meal windows; skip.
            var inside = windows.Any(w => Contains(w, slot.CateringSlot));
            if (!inside)
            {
                result.Warnings.Add(new ScheduleWarning
                {
                    Type = ScheduleWarningType.CateringOutsideHours,
                    Message = $"Catering for slot {slot.Id} falls outside every active meal window.",
                    SlotId = slot.Id,
                });
            }
        }

        // Venue-open window validation for LOAD_IN_VENUE events.
        foreach (var slot in ctx.Order.Slots)
        {
            foreach (var ev in slot.PreShowEvents.Where(e => e.EventType == TimingEventType.LOAD_IN_VENUE))
            {
                if (ev.StartTime is not DateTime evStart) continue;
                var dur = ev.DurationMinutes ?? options.DefaultLoadInVenueMinutes;
                var evEnd = evStart.AddMinutes(dur);
                if (ctx.EffectiveVenueOpen is DateTime vo && evStart < vo)
                {
                    result.Warnings.Add(new ScheduleWarning
                    {
                        Type = ScheduleWarningType.VenueClosed,
                        Message = $"Load-in starts {evStart:HH:mm} before venue open {vo:HH:mm}.",
                        SlotId = slot.Id,
                    });
                }
                if (ctx.EffectiveVenueClose is DateTime vc && evEnd > vc)
                {
                    result.Warnings.Add(new ScheduleWarning
                    {
                        Type = ScheduleWarningType.VenueClosed,
                        Message = $"Load-in ends {evEnd:HH:mm} after venue close {vc:HH:mm}.",
                        SlotId = slot.Id,
                    });
                }
            }
        }
    }

    private static bool Contains(TimeSlot window, TimeSlot value)
    {
        if (window.End is null)
        {
            // Point-in-time meal: accept when slot start matches the meal start.
            return value.Start == window.Start;
        }
        if (value.End is null)
        {
            return value.Start >= window.Start && value.Start <= window.End.Value;
        }
        return value.Start >= window.Start && value.End.Value <= window.End.Value;
    }

    private static int ChangeoverMinutes(RunningOrderSlot slot, VenueTimingOptions options)
    {
        var co = slot.PreShowEvents.FirstOrDefault(e => e.EventType == TimingEventType.CHANGEOVER);
        return co?.DurationMinutes ?? options.DefaultChangeoverMinutes;
    }

    private static void SeedSlotEvents(RunningOrderSlot slot, Ctx ctx)
    {
        var opts = ctx.EffectiveVenueOptions;
        if (slot.OnStageTime is not DateTime onStage) return;
        var cursor = onStage;
        if (opts.IncludePreShowLinecheck)
            SeedBackward(slot, TimingEventType.PRESHOW_LINECHECK, ref cursor, opts.DefaultPreShowLinecheckMinutes);
        if (opts.IncludeSoundcheck)
            SeedBackward(slot, TimingEventType.SOUNDCHECK, ref cursor, opts.DefaultSoundcheckMinutes);
        if (opts.IncludeSetupOnStage)
            SeedBackward(slot, TimingEventType.SETUP_ON_STAGE, ref cursor, opts.DefaultSetupOnStageMinutes);
        if (opts.IncludeStageLoadIn)
            SeedBackward(slot, TimingEventType.LOAD_IN_STAGE, ref cursor, opts.DefaultStageLoadInMinutes);
        if (opts.IncludeGetIn)
            SeedBackward(slot, TimingEventType.GET_IN, ref cursor, opts.DefaultGetInMinutes);
    }

    private static void SeedBackward(RunningOrderSlot slot, TimingEventType type, ref DateTime cursor, int duration)
    {
        if (slot.PreShowEvents.Any(e => e.EventType == type)) return;
        var start = cursor.AddMinutes(-duration);
        slot.PreShowEvents.Add(new SlotTimingEvent
        {
            EventType = type,
            StartTime = start,
            DurationMinutes = null,
            IsPinned = false,
        });
        cursor = start;
    }

    private static RunningOrderSlot CloneSlot(RunningOrderSlot s) => new()
    {
        Id = s.Id,
        BandId = s.BandId,
        StageId = s.StageId,
        OnStageTime = s.OnStageTime,
        IsOnStagePinned = s.IsOnStagePinned,
        SetLengthMinutes = s.SetLengthMinutes,
        SoundcheckOrderIndex = s.SoundcheckOrderIndex,
        EarlyChain = s.EarlyChain.Select(CloneEvent).ToList(),
        PreShowEvents = s.PreShowEvents.Select(CloneEvent).ToList(),
        PostShowEvents = s.PostShowEvents.Select(CloneEvent).ToList(),
        BackstageTime = s.BackstageTime,
        IsBackstageTimePinned = s.IsBackstageTimePinned,
        BackstageLeadMinutes = s.BackstageLeadMinutes,
        BackstageCurfewTime = s.BackstageCurfewTime,
        IsBackstageCurfewPinned = s.IsBackstageCurfewPinned,
        CateringSlot = s.CateringSlot is null ? null : new TimeSlot { Start = s.CateringSlot.Start, End = s.CateringSlot.End },
        Flags = s.Flags,
        OverrideFlags = s.OverrideFlags,
        Notes = s.Notes,
    };

    private static SlotTimingEvent CloneEvent(SlotTimingEvent e) => new()
    {
        EventType = e.EventType,
        StartTime = e.StartTime,
        DurationMinutes = e.DurationMinutes,
        IsPinned = e.IsPinned,
    };

    private static void ReplaceFromSnapshot(RunningOrderSlot live, RunningOrderSlot snap)
    {
        live.BandId = snap.BandId;
        live.StageId = snap.StageId;
        live.OnStageTime = snap.OnStageTime;
        live.IsOnStagePinned = snap.IsOnStagePinned;
        live.SetLengthMinutes = snap.SetLengthMinutes;
        live.SoundcheckOrderIndex = snap.SoundcheckOrderIndex;
        live.EarlyChain = snap.EarlyChain;
        live.PreShowEvents = snap.PreShowEvents;
        live.PostShowEvents = snap.PostShowEvents;
        live.BackstageTime = snap.BackstageTime;
        live.IsBackstageTimePinned = snap.IsBackstageTimePinned;
        live.BackstageLeadMinutes = snap.BackstageLeadMinutes;
        live.BackstageCurfewTime = snap.BackstageCurfewTime;
        live.IsBackstageCurfewPinned = snap.IsBackstageCurfewPinned;
        live.CateringSlot = snap.CateringSlot;
        live.Flags = snap.Flags;
        live.OverrideFlags = snap.OverrideFlags;
        live.Notes = snap.Notes;
    }

    // -------- context --------

    private sealed class Ctx
    {
        public RunningOrder Order { get; }
        public ShowData Show { get; }
        public DateTime BaseDate { get; }
        public ScheduleMode Mode { get; }
        public TimingEventType AnchorEvent { get; }
        public VenueTimingOptions EffectiveVenueOptions { get; }

        public DateTime? EffectiveVenueOpen { get; }
        public DateTime? EffectiveVenueClose { get; }
        public DateTime? EffectiveTechnicalGetIn { get; }
        public DateTime? EffectiveDoors { get; }
        public DateTime? EffectiveFirstShow { get; }
        public DateTime? EffectiveSoundCurfew { get; }
        public DateTime? EffectiveBackstageCurfew { get; }
        public TimeSlot? EffectiveBreakfast { get; }
        public TimeSlot? EffectiveLunch { get; }
        public TimeSlot? EffectiveDinner { get; }
        public int EffectiveBreakTime { get; }
        public int EffectiveSoundcheckGap { get; }

        public Ctx(RunningOrder order, ShowData show)
        {
            Order = order;
            Show = show;
            BaseDate = show.DateOfOpening == default
                ? DateTime.Today
                : show.DateOfOpening.AddDays(Math.Max(0, order.ShowDayNumber - 1)).ToDateTime(TimeOnly.MinValue);
            Mode = order.ModeOverride ?? show.DefaultScheduleMode;
            AnchorEvent = order.AnchorEventOverride ?? show.DefaultAnchorEvent;
            if (AnchorEvent == default) AnchorEvent = TimingEventType.ON_STAGE;
            EffectiveVenueOptions = order.VenueOptions ?? new VenueTimingOptions();
            EffectiveVenueOpen = order.VenueOpenTimeOverride ?? show.VenueOpenTime;
            EffectiveVenueClose = order.VenueCloseTimeOverride ?? show.VenueCloseTime;
            EffectiveTechnicalGetIn = order.TechnicalGetInTimeOverride ?? show.TechnicalGetInTime;
            EffectiveDoors = order.DoorsOpeningTimeOverride ?? show.DoorsOpeningTime;
            EffectiveFirstShow = order.FirstShowTimeOverride ?? show.FirstShowTime;
            EffectiveSoundCurfew = order.SoundCurfewTimeOverride ?? show.SoundCurfewTime;
            EffectiveBackstageCurfew = order.BackstageCurfewTimeOverride ?? show.BackstageCurfewTime;
            EffectiveBreakfast = order.BreakfastHoursOverride ?? show.BreakfastHours;
            EffectiveLunch = order.LunchHoursOverride ?? show.LunchHours;
            EffectiveDinner = order.DinnerHoursOverride ?? show.DinnerHours;
            EffectiveBreakTime = order.BreakTimeMinutesOverride ?? show.BreakTimeMinutes;
            EffectiveSoundcheckGap = order.SoundcheckGapMinutesOverride ?? show.SoundcheckGapMinutes;
        }
    }
}
