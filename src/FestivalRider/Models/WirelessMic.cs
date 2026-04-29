namespace FestivalRider.Models;

public class WirelessMic
{
    public string Where { get; set; } = string.Empty;
    public int Count { get; set; }
    public CableProvider Provider { get; set; }
    public string? Model { get; set; }
    public string? Frequency { get; set; }
}
