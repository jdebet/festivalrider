namespace FestivalRider.Models;

public class FohSound
{
    public string? OwnConsoleModel { get; set; }
    public OutputProtocol OutputProtocol { get; set; }
    public OutputLocation OutputLocation { get; set; }
    public string? OutputNotes { get; set; }
    public string? AdditionalHardware { get; set; }
    public int StageToFohSendCount { get; set; }
    public bool StageToFohRoundTrip { get; set; }
    public decimal? FootprintWidthMeters { get; set; }
    public decimal? FootprintLengthMeters { get; set; }
    public string? Notes { get; set; }
}
