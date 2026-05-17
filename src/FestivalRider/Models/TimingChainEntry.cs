namespace FestivalRider.Models;

public class TimingChainEntry
{
    public TimingEventType EventType { get; set; }
    public string? CustomDisplayName { get; set; }
    public int DefaultDurationMinutes { get; set; }
    public bool IsOptional { get; set; }
}
