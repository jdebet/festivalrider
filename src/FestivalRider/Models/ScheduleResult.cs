namespace FestivalRider.Models;

public class ScheduleResult
{
    public bool Success { get; set; } = true;
    public List<ScheduleWarning> Warnings { get; set; } = new();
}
