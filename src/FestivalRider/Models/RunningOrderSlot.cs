namespace FestivalRider.Models;

public record RunningOrderSlot(
    Guid BandId,
    int StageId,
    TimeOnly StartTime,
    int SetLengthMinutes,
    int ChangeoverMinutes,
    string? Notes);
