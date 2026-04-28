namespace FestivalRider.Models;

public class LightingRig
{
    public string? OwnConsoleModel { get; set; }
    public List<LightingMachine> FloorMachines { get; set; } = new();
    public decimal? BackdropWidthMeters { get; set; }
    public decimal? BackdropHeightMeters { get; set; }
}
