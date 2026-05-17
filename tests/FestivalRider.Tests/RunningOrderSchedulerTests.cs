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

        var a = AddSlot(ro, 1, first, 60, pinned: true, sortIndex: 1);
        var b = AddSlot(ro, 1, first.AddMinutes(75), 60, pinned: true, sortIndex: 0);

        var result = sut.Recalculate(ro, show);

        Assert.Empty(result.Warnings);
        var soundcheckA = a.PreShowEvents.Single(e => e.EventType == TimingEventType.SOUNDCHECK);
        var soundcheckB = b.PreShowEvents.Single(e => e.EventType == TimingEventType.SOUNDCHECK);
        // breakAnchor = doors? null -> firstShow 20:00 - 120 = 18:00. Default soundcheck = 30 min.
        // SoundcheckOrderIndex pack order: 0 (b) first, 1 (a) second. With gap 0, both end at 18:00.
        // SoundcheckOrderIndex 0 (b) packs first (ends at 18:00), index 1 (a) packs after it.
        Assert.Equal(new DateTime(2024, 6, 15, 17, 30, 0), soundcheckB.StartTime);
        Assert.Equal(new DateTime(2024, 6, 15, 17, 0, 0), soundcheckA.StartTime);
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

        Assert.Equal(first.AddMinutes(-15), slot.BackstageTime);
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
}
