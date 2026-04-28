namespace FestivalRider.Models;

public class Cable
{
    public CablePoint Source { get; set; }
    public string? SourceOther { get; set; }
    public CablePoint Target { get; set; }
    public string? TargetOther { get; set; }
    public CableType Type { get; set; } = CableType.Rj45;
    public string? TypeOther { get; set; }
    public string? CategoryOrSpec { get; set; }
    public decimal? MinLengthMeters { get; set; }
    public CableProvider Provider { get; set; }
}
