using FestivalRider.Models;

namespace FestivalRider.Services;

public interface IRunningOrderScheduler
{
    ScheduleResult Recalculate(RunningOrder order, ShowData show);
    ScheduleResult AddSlot(RunningOrder order, BandPlacement placement, ShowData show);
    ScheduleResult RemoveSlot(RunningOrder order, Guid slotId, ShowData show);
    ScheduleResult MoveSlot(RunningOrder order, Guid slotId, int newIndex, ShowData show);
    ScheduleResult SetSoundcheckOrder(RunningOrder order, Guid slotId, int newSoundcheckIndex, ShowData show);
    List<ScheduleWarning> Validate(RunningOrder order, ShowData show);
}
