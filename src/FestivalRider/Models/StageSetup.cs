namespace FestivalRider.Models;

public class StageSetup
{
    public List<Riser> Risers { get; set; } = new();
    public List<OtherRiser> OtherRisers { get; set; } = new();
    public List<WirelessMic> WirelessMics { get; set; } = new();
    public bool BringsOwnMics { get; set; }
    public string? Notes { get; set; }
}
