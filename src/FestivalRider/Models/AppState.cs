namespace FestivalRider.Models;

public class AppState
{
    public int SchemaVersion { get; set; } = 4;
    public List<ShowData> Shows { get; set; } = new();
    public Guid ActiveShowId { get; set; }
    public List<Band> Bands { get; set; } = new();
    public List<RunningOrder> RunningOrders { get; set; } = new();

    public AppState()
    {
        var seed = new ShowData { Name = "Untitled show" };
        Shows.Add(seed);
        ActiveShowId = seed.Id;
    }
}
