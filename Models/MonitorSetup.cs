namespace FestivalRider.Models;

public class MonitorSetup
{
    public MonitorSourceMode SourceMode { get; set; } = MonitorSourceMode.None;
    public string? OwnConsoleModel { get; set; }
    public MonitorTechLocation OwnConsoleLocation { get; set; }
    public List<MonitorWedge> Wedges { get; set; } = new();
    public List<InEarMonitor> InEars { get; set; } = new();
    public string? Notes { get; set; }
}
