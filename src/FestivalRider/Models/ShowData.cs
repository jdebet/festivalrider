using System.ComponentModel.DataAnnotations;

namespace FestivalRider.Models;

public class ShowData
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }
    public DateOnly DateOfOpening { get; set; }

    [Range(1, 31)]
    public int ShowDayCount { get; set; } = 1;

    public List<Stage> Stages { get; set; } = new();
    public List<Band> Bands { get; set; } = new();
    public List<RunningOrder> RunningOrders { get; set; } = new();

    public ScheduleMode DefaultScheduleMode { get; set; } = ScheduleMode.Traditional;
    public TimingEventType DefaultAnchorEvent { get; set; } = TimingEventType.ON_STAGE;
    public DateTime? VenueOpenTime { get; set; }
    public DateTime? VenueCloseTime { get; set; }
    public DateTime? TechnicalGetInTime { get; set; }
    public DateTime? DoorsOpeningTime { get; set; }
    public DateTime? FirstShowTime { get; set; }
    public DateTime? SoundCurfewTime { get; set; }
    public DateTime? BackstageCurfewTime { get; set; }
    public TimeSlot? BreakfastHours { get; set; }
    public TimeSlot? LunchHours { get; set; }
    public TimeSlot? DinnerHours { get; set; }
    public int BreakTimeMinutes { get; set; } = 120;
    public int SoundcheckGapMinutes { get; set; } = 0;
    public List<StageLinkGroup> StageLinkGroups { get; set; } = new();
}
