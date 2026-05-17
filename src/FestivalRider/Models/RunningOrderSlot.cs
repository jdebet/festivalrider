namespace FestivalRider.Models;

public class RunningOrderSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public int StageId { get; set; }
    public DateTime? OnStageTime { get; set; }
    public bool IsOnStagePinned { get; set; }
    public int? SetLengthMinutes { get; set; }
    public int SoundcheckOrderIndex { get; set; }
    public List<SlotTimingEvent> EarlyChain { get; set; } = new();
    public List<SlotTimingEvent> PreShowEvents { get; set; } = new();
    public List<SlotTimingEvent> PostShowEvents { get; set; } = new();
    public DateTime? BackstageTime { get; set; }
    public bool IsBackstageTimePinned { get; set; }
    public int? BackstageLeadMinutes { get; set; }
    public DateTime? BackstageCurfewTime { get; set; }
    public bool IsBackstageCurfewPinned { get; set; }
    public TimeSlot? CateringSlot { get; set; }
    public BandScheduleFlags Flags { get; set; }
    public UserOverrideFlags OverrideFlags { get; set; }
    public string? Notes { get; set; }
}
