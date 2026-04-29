namespace FestivalRider.Models;

public class InEarMonitor
{
    public string Where { get; set; } = string.Empty;
    public bool IsWireless { get; set; }
    public CableProvider Provider { get; set; }
    public string? Model { get; set; }
    public string? Frequency { get; set; }
}
