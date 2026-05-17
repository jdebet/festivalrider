namespace FestivalRider.Models;

public class ScheduleWarning
{
    public ScheduleWarningType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? SlotId { get; set; }
    public Guid? RelatedSlotId { get; set; }
}
