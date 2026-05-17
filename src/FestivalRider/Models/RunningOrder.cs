using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class RunningOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ShowId { get; set; }

    [Range(1, 31)]
    public int ShowDayNumber { get; set; } = 1;

    public List<RunningOrderSlot> Slots { get; set; } = new();

    public ScheduleMode? ModeOverride { get; set; }
    public TimingEventType? AnchorEventOverride { get; set; }
    public DateTime? VenueOpenTimeOverride { get; set; }
    public DateTime? VenueCloseTimeOverride { get; set; }
    public DateTime? TechnicalGetInTimeOverride { get; set; }
    public DateTime? DoorsOpeningTimeOverride { get; set; }
    public DateTime? FirstShowTimeOverride { get; set; }
    public DateTime? SoundCurfewTimeOverride { get; set; }
    public DateTime? BackstageCurfewTimeOverride { get; set; }
    public TimeSlot? BreakfastHoursOverride { get; set; }
    public TimeSlot? LunchHoursOverride { get; set; }
    public TimeSlot? DinnerHoursOverride { get; set; }
    public int? BreakTimeMinutesOverride { get; set; }
    public int? SoundcheckGapMinutesOverride { get; set; }
    public VenueTimingOptions? VenueOptions { get; set; }
    public FestivalTimingTemplate? FestivalTemplate { get; set; }
}
