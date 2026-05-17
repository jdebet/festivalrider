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
    public DateTime? VenueOpenTime { get; set; } = DateTime.Today.AddHours(14);
    public DateTime? VenueCloseTime { get; set; } = DateTime.Today.AddHours(23).AddMinutes(45);
    public DateTime? TechnicalGetInTime { get; set; } = DateTime.Today.AddHours(15);
    public DateTime? DoorsOpeningTime { get; set; } = DateTime.Today.AddHours(19).AddMinutes(30);
    public DateTime? FirstShowTime { get; set; } = DateTime.Today.AddHours(20);
    public DateTime? SoundCurfewTime { get; set; } = DateTime.Today.AddHours(23);
    public DateTime? BackstageCurfewTime { get; set; } = DateTime.Today.AddHours(23).AddMinutes(30);
    public TimeSlot? BreakfastHours { get; set; } = new() { Start = DateTime.Today.AddHours(8), End = DateTime.Today.AddHours(10) };
    public TimeSlot? LunchHours { get; set; } = new() { Start = DateTime.Today.AddHours(12), End = DateTime.Today.AddHours(14) };
    public TimeSlot? DinnerHours { get; set; } = new() { Start = DateTime.Today.AddHours(18), End = DateTime.Today.AddHours(19) };
    public int BreakTimeMinutes { get; set; } = 120;
    public int SoundcheckGapMinutes { get; set; } = 0;
    public List<StageLinkGroup> StageLinkGroups { get; set; } = new();
}
