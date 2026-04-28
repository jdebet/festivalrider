namespace FestivalRider.Models;

public class TechRider
{
    public List<Cable> Cables { get; set; } = new();
    public LightingRig Lighting { get; set; } = new();
    public PowerRequirements Power { get; set; } = new();
    public FohSound Foh { get; set; } = new();
    public MonitorSetup Monitors { get; set; } = new();
    public StageSetup Stage { get; set; } = new();
    public string? Notes { get; set; }
}
