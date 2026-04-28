namespace FestivalRider.Models;

public class Rider
{
    public TechRider Tech { get; set; } = new();
    public HospitalityRider Hospitality { get; set; } = new();
}
