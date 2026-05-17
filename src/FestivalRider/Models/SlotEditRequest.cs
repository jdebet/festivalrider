namespace FestivalRider.Models;

public class SlotEditRequest
{
    public Guid SlotId { get; set; }
    public TimingEventType EventType { get; set; }
    public DateTime? NewStartTime { get; set; }
    public bool ToggledPin { get; set; }
}
