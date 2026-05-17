namespace FestivalRider.Models;

public class BandPlacement
{
    public Guid BandId { get; set; }
    public int StageId { get; set; }
    public int? InsertAtIndex { get; set; }
    public DateTime? PinnedOnStageTime { get; set; }
}
