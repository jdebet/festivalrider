namespace FestivalRider.Models;

public class PowerRequirements
{
    public PowerAmperage Amperage { get; set; } = PowerAmperage._16_A;
    public PowerPhase Phase { get; set; } = PowerPhase.SinglePhase;
    public string? AdapterNotes { get; set; }
}
