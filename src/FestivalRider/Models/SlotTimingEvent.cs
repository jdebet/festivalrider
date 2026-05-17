namespace FestivalRider.Models;

public class SlotTimingEvent
{
    public TimingEventType EventType { get; set; }
    public DateTime? StartTime { get; set; }
    public int? DurationMinutes { get; set; }
    public bool IsPinned { get; set; }
}
