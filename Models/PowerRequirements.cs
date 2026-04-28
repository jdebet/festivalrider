namespace FestivalRider.Models;

public class PowerRequirements
{
    public PowerAmperage Amperage { get; set; } = PowerAmperage.A16;
    public PowerPhase Phase { get; set; } = PowerPhase.SinglePhase;
    public string? AdapterNotes { get; set; }
}
