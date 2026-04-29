namespace FestivalRider.Models;

public class AppState
{
    public int SchemaVersion { get; set; } = 2;
    public ShowData ShowData { get; set; } = new();
    public List<Band> Bands { get; set; } = new();
    public List<RunningOrder> RunningOrders { get; set; } = new();
}
