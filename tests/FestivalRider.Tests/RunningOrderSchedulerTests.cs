using FestivalRider.Models;
using FestivalRider.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FestivalRider.Tests;

public sealed class RunningOrderSchedulerTests
{
    private static IRunningOrderScheduler Create() =>
        new RunningOrderScheduler(NullLogger<RunningOrderScheduler>.Instance);

    private static (ShowData show, RunningOrder ro) MakeShow(
        DateTime firstShow,
        int breakMinutes = 120,
        int gap = 0)
    {
        var date = DateOnly.FromDateTime(firstShow);
        var show = new ShowData
        {
            Name = "Test",
            DateOfOpening = date,
            ShowDayCount = 1,
            FirstShowTime = firstShow,
            BreakTimeMinutes = breakMinutes,
            SoundcheckGapMinutes = gap,
        };
        show.Stages.Add(new Stage { Id = 1, Name = "Main" });
        var ro = new RunningOrder { ShowId = show.Id, ShowDayNumber = 1, VenueOptions = new VenueTimingOptions() };
        show.RunningOrders.Add(ro);
        return (show, ro);
    }

    private static RunningOrderSlot AddSlot(RunningOrder ro, int stageId, DateTime? onStage, int setLen, bool pinned, int sortIndex)
        => AddSlotWithSoundcheck(ro, stageId, onStage, setLen, pinned, sortIndex, includeSoundcheck: true);

    private static RunningOrderSlot AddSlotWithSoundcheck(RunningOrder ro, int stageId, DateTime? onStage, int setLen, bool pinned, int sortIndex, bool includeSoundcheck)
    {
        var slot = new RunningOrderSlot
        {
            BandId = Guid.NewGuid(),
            StageId = stageId,
            OnStageTime = onStage,
            IsOnStagePinned = pinned,
            SetLengthMinutes = setLen,
            SoundcheckOrderIndex = sortIndex,
        };
        if (includeSoundcheck)
        {
            slot.PreShowEvents.Add(new SlotTimingEvent { EventType = TimingEventType.SOUNDCHECK });
        }
        ro.Slots.Add(slot);
        return slot;
    }

    [Fact]
    public void Recalculate_Traditional_PackBackwardSoundchecks()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first, breakMinutes: 120);
        show.TechnicalGetInTime = first.AddHours(-4); // 16:00 on show date, before soundchecks

        var a = AddSlot(ro, 1, first, 60, pinned: true, sortIndex: 1);
        var b = AddSlot(ro, 1, first.AddMinutes(75), 60, pinned: true, sortIndex: 0);

        var result = sut.Recalculate(ro, show);

        Assert.Empty(result.Warnings);
        var soundcheckA = a.PreShowEvents.Single(e => e.EventType == TimingEventType.SOUNDCHECK);
        var soundcheckB = b.PreShowEvents.Single(e => e.EventType == TimingEventType.SOUNDCHECK);
        // breakAnchor = doors? null -> firstShow 20:00 - 120 = 18:00. Default soundcheck = 30 min.
        // Show-order packing: A packs first (ends at 18:00, starts at 17:30).
        // scCursor after A = setupStart(17:15). B packs next (ends at 17:15, starts at 16:45).
        Assert.Equal(new DateTime(2024, 6, 15, 17, 30, 0), soundcheckA.StartTime);
        Assert.Equal(new DateTime(2024, 6, 15, 16, 45, 0), soundcheckB.StartTime);
    }

    [Fact]
    public void Recalculate_Traditional_ForwardCascade_FromFirstShow()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 19, 0, 0);
        var (show, ro) = MakeShow(first);
        var s1 = AddSlot(ro, 1, null, 60, pinned: false, sortIndex: 0);
        var s2 = AddSlot(ro, 1, null, 45, pinned: false, sortIndex: 1);

        sut.Recalculate(ro, show);

        Assert.Equal(first, s1.OnStageTime);
        // s1 ends at 20:00, plus default 15-min changeover -> s2 starts at 20:15
        Assert.Equal(new DateTime(2024, 6, 15, 20, 15, 0), s2.OnStageTime);
    }

    [Fact]
    public void Recalculate_Traditional_OnStageOverlap_EmitsWarning()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 19, 0, 0);
        var (show, ro) = MakeShow(first);
        var s1 = AddSlot(ro, 1, first, 90, pinned: true, sortIndex: 0);
        var s2 = AddSlot(ro, 1, first.AddMinutes(60), 60, pinned: true, sortIndex: 1);

        var result = sut.Recalculate(ro, show);

        Assert.Contains(result.Warnings, w => w.Type == ScheduleWarningType.OnStageOverlap);
    }

    [Fact]
    public void Recalculate_Traditional_AllowOnStageOverlap_DowngradedWarning()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 19, 0, 0);
        var (show, ro) = MakeShow(first);
        AddSlot(ro, 1, first, 90, pinned: true, sortIndex: 0);
        var s2 = AddSlot(ro, 1, first.AddMinutes(60), 60, pinned: true, sortIndex: 1);
        s2.OverrideFlags = UserOverrideFlags.AllowOnStageOverlap;

        var result = sut.Recalculate(ro, show);

        Assert.Contains(result.Warnings, w => w.Type == ScheduleWarningType.UserOverrideOverlap);
        Assert.DoesNotContain(result.Warnings, w => w.Type == ScheduleWarningType.OnStageOverlap);
    }

    [Fact]
    public void Recalculate_Traditional_FirstShowTimeMissing_FallbackAndWarning()
    {
        var sut = Create();
        var date = new DateOnly(2024, 6, 15);
        var show = new ShowData { Name = "x", DateOfOpening = date, ShowDayCount = 1 };
        show.FirstShowTime = null; // clear default so scheduler emits missing warning
        show.Stages.Add(new Stage { Id = 1, Name = "Main" });
        var ro = new RunningOrder { ShowId = show.Id, ShowDayNumber = 1, VenueOptions = new VenueTimingOptions() };
        AddSlot(ro, 1, null, 60, pinned: false, sortIndex: 0);

        var result = sut.Recalculate(ro, show);

        Assert.Single(result.Warnings, w => w.Type == ScheduleWarningType.FirstShowTimeMissing);
        Assert.Equal(date.ToDateTime(new TimeOnly(20, 0)), ro.Slots[0].OnStageTime);
    }

    [Fact]
    public void Recalculate_Traditional_SoundCurfew_Violation()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 22, 0, 0);
        var (show, ro) = MakeShow(first);
        show.SoundCurfewTime = new DateTime(2024, 6, 15, 22, 30, 0);
        AddSlot(ro, 1, first, 60, pinned: true, sortIndex: 0);

        var result = sut.Recalculate(ro, show);

        Assert.Contains(result.Warnings, w => w.Type == ScheduleWarningType.CurfewViolation);
    }

    [Fact]
    public void Recalculate_Traditional_BackstageTime_DerivedFromOnStageMinusLead()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first);
        var slot = AddSlot(ro, 1, first, 60, pinned: true, sortIndex: 0);
        slot.BackstageLeadMinutes = 15;

        sut.Recalculate(ro, show);

        // BackstageTime anchors to the earliest of PRESHOW_LINECHECK and ON_STAGE, minus lead.
        // PRESHOW_LINECHECK is seeded 10 min before ON_STAGE at 20:00 → 19:50.
        Assert.Equal(first.AddMinutes(-10 - 15), slot.BackstageTime);
    }

    [Fact]
    public void Recalculate_Traditional_IsIdempotent()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first);
        AddSlot(ro, 1, first, 60, pinned: true, sortIndex: 0);
        AddSlot(ro, 1, first.AddMinutes(75), 60, pinned: true, sortIndex: 1);

        var r1 = sut.Recalculate(ro, show);
        var snapshot = ro.Slots.Select(s => (s.OnStageTime, s.BackstageTime,
            sc: s.PreShowEvents.FirstOrDefault(e => e.EventType == TimingEventType.SOUNDCHECK)?.StartTime)).ToList();
        var r2 = sut.Recalculate(ro, show);

        Assert.Equal(r1.Warnings.Count, r2.Warnings.Count);
        for (int i = 0; i < ro.Slots.Count; i++)
        {
            var s = ro.Slots[i];
            Assert.Equal(snapshot[i].OnStageTime, s.OnStageTime);
            Assert.Equal(snapshot[i].BackstageTime, s.BackstageTime);
            Assert.Equal(snapshot[i].sc,
                s.PreShowEvents.FirstOrDefault(e => e.EventType == TimingEventType.SOUNDCHECK)?.StartTime);
        }
    }

    [Fact]
    public void AddSlot_PinnedTime_Honoured_AndSeedsPreShowEvents()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first);

        sut.AddSlot(ro, new BandPlacement
        {
            BandId = Guid.NewGuid(),
            StageId = 1,
            PinnedOnStageTime = first,
        }, show);

        var slot = Assert.Single(ro.Slots);
        Assert.True(slot.IsOnStagePinned);
        Assert.Equal(first, slot.OnStageTime);
        Assert.Contains(slot.PreShowEvents, e => e.EventType == TimingEventType.SOUNDCHECK);
        Assert.Contains(slot.PreShowEvents, e => e.EventType == TimingEventType.PRESHOW_LINECHECK);
    }

    [Fact]
    public void RemoveSlot_MissingId_ReturnsConstraintViolation()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first);
        AddSlot(ro, 1, first, 60, pinned: true, sortIndex: 0);

        var result = sut.RemoveSlot(ro, Guid.NewGuid(), show);

        Assert.Contains(result.Warnings, w => w.Type == ScheduleWarningType.ConstraintViolation);
        Assert.Single(ro.Slots);
    }

    [Fact]
    public void MoveSlot_ReordersByStableId()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first);
        var a = AddSlot(ro, 1, first, 60, pinned: false, sortIndex: 0);
        var b = AddSlot(ro, 1, null, 45, pinned: false, sortIndex: 1);

        sut.MoveSlot(ro, b.Id, 0, show);

        Assert.Equal(b.Id, ro.Slots[0].Id);
        Assert.Equal(a.Id, ro.Slots[1].Id);
    }

    [Fact]
    public void Validate_DoesNotMutateGraph()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first);
        AddSlot(ro, 1, first, 60, pinned: true, sortIndex: 0);
        var beforeOnStage = ro.Slots[0].OnStageTime;
        var beforeBackstage = ro.Slots[0].BackstageTime;
        var beforeEvents = ro.Slots[0].PreShowEvents.Count;

        var warnings = sut.Validate(ro, show);

        Assert.Equal(beforeOnStage, ro.Slots[0].OnStageTime);
        Assert.Equal(beforeBackstage, ro.Slots[0].BackstageTime);
        Assert.Equal(beforeEvents, ro.Slots[0].PreShowEvents.Count);
        _ = warnings;
    }

    // -------- festival helpers --------

    private static (ShowData show, RunningOrder ro) MakeFestivalShow(
        DateTime firstShow,
        FestivalTimingTemplate? template = null,
        ScheduleMode showMode = ScheduleMode.Traditional)
    {
        var date = DateOnly.FromDateTime(firstShow);
        var show = new ShowData
        {
            Name = "Test",
            DateOfOpening = date,
            ShowDayCount = 1,
            FirstShowTime = firstShow,
            DefaultScheduleMode = showMode,
            DefaultAnchorEvent = TimingEventType.ON_STAGE,
        };
        show.Stages.Add(new Stage { Id = 1, Name = "Main" });
        show.Stages.Add(new Stage { Id = 2, Name = "Tent" });
        var ro = new RunningOrder
        {
            ShowId = show.Id,
            ShowDayNumber = 1,
            ModeOverride = ScheduleMode.Festival,
            FestivalTemplate = template ?? new FestivalTimingTemplate(),
        };
        show.RunningOrders.Add(ro);
        return (show, ro);
    }

    private static RunningOrderSlot AddFestivalSlot(RunningOrder ro, int stageId, DateTime? onStage, int setLen, bool pinned)
    {
        var slot = new RunningOrderSlot
        {
            BandId = Guid.NewGuid(),
            StageId = stageId,
            OnStageTime = onStage,
            IsOnStagePinned = pinned,
            SetLengthMinutes = setLen,
        };
        ro.Slots.Add(slot);
        return slot;
    }

    // -------- festival pipeline tests --------

    [Fact]
    public void Recalculate_Festival_TemplateDrivenChainGrowth()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate
        {
            PreShowEntries =
            {
                new TimingChainEntry { EventType = TimingEventType.PRESHOW_LINECHECK, DefaultDurationMinutes = 10 },
                new TimingChainEntry { EventType = TimingEventType.SOUNDCHECK, DefaultDurationMinutes = 30 },
            },
            PostShowEntries =
            {
                new TimingChainEntry { EventType = TimingEventType.LOAD_OUT_STAGING, DefaultDurationMinutes = 20 },
            },
            DefaultSetLengthMinutes = 60,
        };
        var (show, ro) = MakeFestivalShow(first, template);
        AddFestivalSlot(ro, 1, first, 60, pinned: true);

        var result = sut.Recalculate(ro, show);

        Assert.Empty(result.Warnings);
        var slot = ro.Slots[0];
        // Pre-show: SOUNDCHECK (30 min) then PRESHOW_LINECHECK (10 min), both before ON_STAGE at 20:00.
        var linecheck = slot.PreShowEvents.First(e => e.EventType == TimingEventType.PRESHOW_LINECHECK);
        var soundcheck = slot.PreShowEvents.First(e => e.EventType == TimingEventType.SOUNDCHECK);
        // PRESHOW_LINECHECK closest to anchor: ends at 20:00, starts at 19:50.
        Assert.Equal(new DateTime(2024, 6, 15, 19, 50, 0), linecheck.StartTime);
        // SOUNDCHECK next: ends at 19:50, starts at 19:20.
        Assert.Equal(new DateTime(2024, 6, 15, 19, 20, 0), soundcheck.StartTime);
        // Post-show: LOAD_OUT_STAGING starts at 21:00 (after 60 min set).
        var loadOut = slot.PostShowEvents.First(e => e.EventType == TimingEventType.LOAD_OUT_STAGING);
        Assert.Equal(new DateTime(2024, 6, 15, 21, 0, 0), loadOut.StartTime);
    }

    [Fact]
    public void Recalculate_Festival_MultiStageIndependence_NoOverlapWarning()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate { DefaultSetLengthMinutes = 60 };
        var (show, ro) = MakeFestivalShow(first, template);
        AddFestivalSlot(ro, 1, first, 60, pinned: true);
        AddFestivalSlot(ro, 2, first, 60, pinned: true); // same time, different stage

        var result = sut.Recalculate(ro, show);

        Assert.DoesNotContain(result.Warnings, w => w.Type == ScheduleWarningType.OnStageOverlap);
    }

    [Fact]
    public void Recalculate_Festival_EarlyChainValidation_PassesWhenBeforeOnStage()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate
        {
            EarlyChain =
            {
                new TimingChainEntry { EventType = TimingEventType.SOUNDCHECK, DefaultDurationMinutes = 30 },
            },
            DefaultSetLengthMinutes = 60,
        };
        var (show, ro) = MakeFestivalShow(first, template);
        AddFestivalSlot(ro, 1, first, 60, pinned: true);
        sut.Recalculate(ro, show); // seed EarlyChain from template
        var slot = ro.Slots[0];
        slot.EarlyChain[0].StartTime = new DateTime(2024, 6, 15, 9, 0, 0); // ends at 09:30, well before 20:00
        slot.EarlyChain[0].IsPinned = true;

        var result = sut.Recalculate(ro, show);

        Assert.DoesNotContain(result.Warnings, w => w.Type == ScheduleWarningType.EarlySoundcheckAfterOnStage);
    }

    [Fact]
    public void Recalculate_Festival_EarlyChainValidation_FailsWhenAfterOnStage()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate
        {
            EarlyChain =
            {
                new TimingChainEntry { EventType = TimingEventType.SOUNDCHECK, DefaultDurationMinutes = 30 },
            },
            DefaultSetLengthMinutes = 60,
        };
        var (show, ro) = MakeFestivalShow(first, template);
        AddFestivalSlot(ro, 1, first, 60, pinned: true);
        sut.Recalculate(ro, show); // seed EarlyChain from template
        var slot = ro.Slots[0];
        slot.EarlyChain[0].StartTime = new DateTime(2024, 6, 15, 19, 50, 0); // ends at 20:20, after 20:00
        slot.EarlyChain[0].IsPinned = true;

        var result = sut.Recalculate(ro, show);

        Assert.Contains(result.Warnings, w => w.Type == ScheduleWarningType.EarlySoundcheckAfterOnStage);
    }

    [Fact]
    public void Recalculate_Festival_AnchorFallback_ThreeStep()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);

        // 1. RO override wins.
        var template = new FestivalTimingTemplate { DefaultSetLengthMinutes = 60 };
        var (show1, ro1) = MakeFestivalShow(first, template, showMode: ScheduleMode.Traditional);
        ro1.AnchorEventOverride = TimingEventType.SOUNDCHECK;
        AddFestivalSlot(ro1, 1, first, 60, pinned: true);
        sut.Recalculate(ro1, show1);
        Assert.Equal(TimingEventType.SOUNDCHECK, ro1.Slots[0].PreShowEvents.First(e => e.EventType == TimingEventType.SOUNDCHECK).EventType);

        // 2. Show default wins when RO override is null.
        var (show2, ro2) = MakeFestivalShow(first, template, showMode: ScheduleMode.Traditional);
        show2.DefaultAnchorEvent = TimingEventType.SOUNDCHECK;
        AddFestivalSlot(ro2, 1, first, 60, pinned: true);
        sut.Recalculate(ro2, show2);
        Assert.Equal(TimingEventType.SOUNDCHECK, ro2.Slots[0].PreShowEvents.First(e => e.EventType == TimingEventType.SOUNDCHECK).EventType);

        // 3. Ultimate fallback to ON_STAGE.
        var (show3, ro3) = MakeFestivalShow(first, template, showMode: ScheduleMode.Traditional);
        AddFestivalSlot(ro3, 1, first, 60, pinned: true);
        sut.Recalculate(ro3, show3);
        // ON_STAGE is not in PreShowEvents, so the slot should have no pre-show events when template is empty.
        Assert.Empty(ro3.Slots[0].PreShowEvents);
    }

    [Fact]
    public void Recalculate_Festival_NonOnStageAnchor_DerivesOnStage()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate
        {
            PreShowEntries =
            {
                new TimingChainEntry { EventType = TimingEventType.PRESHOW_LINECHECK, DefaultDurationMinutes = 10 },
            },
            PostShowEntries =
            {
                new TimingChainEntry { EventType = TimingEventType.LOAD_OUT_STAGING, DefaultDurationMinutes = 20 },
            },
            DefaultSetLengthMinutes = 60,
        };
        var (show, ro) = MakeFestivalShow(first, template);
        ro.AnchorEventOverride = TimingEventType.SOUNDCHECK;
        AddFestivalSlot(ro, 1, null, 60, pinned: false);

        sut.Recalculate(ro, show);

        var slot = ro.Slots[0];
        // SOUNDCHECK anchored at first show (20:00), ends at 20:30.
        var soundcheck = slot.PreShowEvents.First(e => e.EventType == TimingEventType.SOUNDCHECK);
        Assert.Equal(first, soundcheck.StartTime);
        // ON_STAGE derived after SOUNDCHECK end + post-show events.
        var loadOut = slot.PostShowEvents.First(e => e.EventType == TimingEventType.LOAD_OUT_STAGING);
        Assert.Equal(new DateTime(2024, 6, 15, 20, 30, 0), loadOut.StartTime);
        Assert.Equal(new DateTime(2024, 6, 15, 20, 50, 0), slot.OnStageTime);
    }

    [Fact]
    public void Recalculate_Festival_AllowOnStageOverlap_DowngradedWarning()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate { DefaultSetLengthMinutes = 60 };
        var (show, ro) = MakeFestivalShow(first, template);
        AddFestivalSlot(ro, 1, first, 90, pinned: true);
        var s2 = AddFestivalSlot(ro, 1, first.AddMinutes(60), 60, pinned: true);
        s2.OverrideFlags = UserOverrideFlags.AllowOnStageOverlap;

        var result = sut.Recalculate(ro, show);

        Assert.Contains(result.Warnings, w => w.Type == ScheduleWarningType.UserOverrideOverlap);
        Assert.DoesNotContain(result.Warnings, w => w.Type == ScheduleWarningType.OnStageOverlap);
    }

    [Fact]
    public void Recalculate_Festival_DuplicateEventTypes_Honoured()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate
        {
            PreShowEntries =
            {
                new TimingChainEntry { EventType = TimingEventType.SOUNDCHECK, DefaultDurationMinutes = 15 },
                new TimingChainEntry { EventType = TimingEventType.SOUNDCHECK, DefaultDurationMinutes = 15 },
            },
            DefaultSetLengthMinutes = 60,
        };
        var (show, ro) = MakeFestivalShow(first, template);
        AddFestivalSlot(ro, 1, first, 60, pinned: true);

        sut.Recalculate(ro, show);

        var soundchecks = ro.Slots[0].PreShowEvents.Where(e => e.EventType == TimingEventType.SOUNDCHECK).ToList();
        Assert.Equal(2, soundchecks.Count);
    }

    [Fact]
    public void Recalculate_Festival_EmptyTemplate_OnlyAnchorEvent()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate(); // empty
        var (show, ro) = MakeFestivalShow(first, template);
        AddFestivalSlot(ro, 1, first, 60, pinned: true);

        var result = sut.Recalculate(ro, show);

        Assert.Empty(result.Warnings);
        Assert.Empty(ro.Slots[0].PreShowEvents);
        Assert.Empty(ro.Slots[0].PostShowEvents);
    }

    [Fact]
    public void Recalculate_Festival_CustomDisplayName_IgnoredByScheduler()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var templateA = new FestivalTimingTemplate
        {
            PreShowEntries =
            {
                new TimingChainEntry { EventType = TimingEventType.SOUNDCHECK, DefaultDurationMinutes = 30, CustomDisplayName = "Morning balance" },
            },
            DefaultSetLengthMinutes = 60,
        };
        var templateB = new FestivalTimingTemplate
        {
            PreShowEntries =
            {
                new TimingChainEntry { EventType = TimingEventType.SOUNDCHECK, DefaultDurationMinutes = 30 },
            },
            DefaultSetLengthMinutes = 60,
        };

        var (showA, roA) = MakeFestivalShow(first, templateA);
        AddFestivalSlot(roA, 1, first, 60, pinned: true);
        sut.Recalculate(roA, showA);

        var (showB, roB) = MakeFestivalShow(first, templateB);
        AddFestivalSlot(roB, 1, first, 60, pinned: true);
        sut.Recalculate(roB, showB);

        var scA = roA.Slots[0].PreShowEvents.First(e => e.EventType == TimingEventType.SOUNDCHECK);
        var scB = roB.Slots[0].PreShowEvents.First(e => e.EventType == TimingEventType.SOUNDCHECK);
        Assert.Equal(scA.StartTime, scB.StartTime);
        Assert.Equal(scA.DurationMinutes, scB.DurationMinutes);
    }

    [Fact]
    public void Recalculate_Festival_NoSoundcheckViaAbsence_SkipsSoundcheck()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var template = new FestivalTimingTemplate
        {
            PreShowEntries =
            {
                new TimingChainEntry { EventType = TimingEventType.PRESHOW_LINECHECK, DefaultDurationMinutes = 10 },
                // No SOUNDCHECK entry
            },
            DefaultSetLengthMinutes = 60,
        };
        var (show, ro) = MakeFestivalShow(first, template);
        AddFestivalSlot(ro, 1, first, 60, pinned: true);

        sut.Recalculate(ro, show);

        Assert.DoesNotContain(ro.Slots[0].PreShowEvents, e => e.EventType == TimingEventType.SOUNDCHECK);
    }

    [Fact]
    public void Recalculate_Traditional_BackstageDrop_Before_LoadInStage()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first);
        ro.VenueOptions = new VenueTimingOptions
        {
            IncludeGetIn = true,
            IncludeLoadInVenue = true,
            IncludeStageLoadIn = true,
            IncludeBackstageDrop = true,
            IncludeSetupOnStage = true,
            IncludeSoundcheck = true,
            IncludePreShowLinecheck = true,
        };
        AddSlot(ro, 1, first, 60, pinned: true, sortIndex: 0);

        sut.Recalculate(ro, show);

        var slot = ro.Slots[0];
        var bd = slot.PreShowEvents.First(e => e.EventType == TimingEventType.BACKSTAGE_DROP);
        var li = slot.PreShowEvents.First(e => e.EventType == TimingEventType.LOAD_IN_STAGE);
        Assert.True(bd.StartTime < li.StartTime, $"BACKSTAGE_DROP ({bd.StartTime}) must start before LOAD_IN_STAGE ({li.StartTime})");
    }

    [Fact]
    public void Recalculate_Traditional_ToggleSetupOff_Cleans_PreShow_And_EarlyChain()
    {
        var sut = Create();
        var first = new DateTime(2024, 6, 15, 20, 0, 0);
        var (show, ro) = MakeShow(first);
        var slot = new RunningOrderSlot
        {
            BandId = Guid.NewGuid(),
            StageId = 1,
            OnStageTime = first,
            IsOnStagePinned = true,
            SetLengthMinutes = 60,
            SoundcheckOrderIndex = 0,
        };
        // Simulate stale EarlyChain data from a prior festival mode.
        slot.EarlyChain.Add(new SlotTimingEvent { EventType = TimingEventType.SETUP_ON_STAGE, StartTime = first.AddHours(-2) });
        slot.PreShowEvents.Add(new SlotTimingEvent { EventType = TimingEventType.SETUP_ON_STAGE });
        ro.Slots.Add(slot);

        ro.VenueOptions = new VenueTimingOptions
        {
            IncludeSetupOnStage = false,
            IncludeSoundcheck = false,
            IncludePreShowLinecheck = false,
        };

        sut.Recalculate(ro, show);

        Assert.DoesNotContain(slot.PreShowEvents, e => e.EventType == TimingEventType.SETUP_ON_STAGE);
        Assert.DoesNotContain(slot.EarlyChain, e => e.EventType == TimingEventType.SETUP_ON_STAGE);
    }
}
