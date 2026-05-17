namespace FestivalRider.Models;

public class AppState
{
    public int SchemaVersion { get; set; } = 6;
    public List<ShowData> Shows { get; set; } = new();
    public Guid ActiveShowId { get; set; }

    public AppState()
    {
        var seed = new ShowData { Name = "Untitled show", Bands = new(), RunningOrders = new() };
        Shows.Add(seed);
        ActiveShowId = seed.Id;
    }
}
