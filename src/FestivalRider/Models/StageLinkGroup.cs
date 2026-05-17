namespace FestivalRider.Models;

public class StageLinkGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<int> StageIds { get; set; } = new();
    public StageLinkConstraint Constraint { get; set; } = StageLinkConstraint.All;
}
