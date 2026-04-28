namespace FestivalRider.Models;

public class InputChannel
{
    public int Number { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? MicPreference { get; set; }
    public string? StandType { get; set; }
    public string? Notes { get; set; }
}
