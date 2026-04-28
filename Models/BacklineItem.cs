namespace FestivalRider.Models;

public class BacklineItem
{
    public BacklineCategory Category { get; set; }
    public string Item { get; set; } = string.Empty;
    public bool ProvidedByVenue { get; set; }
    public string? Notes { get; set; }
}
