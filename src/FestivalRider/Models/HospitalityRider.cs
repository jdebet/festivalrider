namespace FestivalRider.Models;

public class HospitalityRider
{
    public string? DressingRoomNotes { get; set; }
    public string? CateringNotes { get; set; }
    public List<string> DrinksRequests { get; set; } = new();
    public string? DietaryRestrictions { get; set; }
    public int TowelCount { get; set; }
    public int ParkingSpaces { get; set; }
    public string? Accommodations { get; set; }
}
