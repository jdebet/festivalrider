using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

// Sync API: mutations are pure in-memory operations on AppState; no I/O or awaitable work.
public class BandService : IBandService
{
    private readonly ILogger<BandService> _logger;
    private AppState _state = new();

    public BandService(ILogger<BandService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<Band> Bands => _state.Bands;
    public IReadOnlyList<RunningOrder> RunningOrders => _state.RunningOrders;

    public event Action? OnChange;

    public void AddBand(Band band) => throw new NotImplementedException();
    public void UpdateBand(Band band) => throw new NotImplementedException();
    public void DeleteBand(Guid id) => throw new NotImplementedException();
    public Band? FindBand(Guid id) => null;

    public void AddRunningOrder(RunningOrder order) => throw new NotImplementedException();
    public void UpdateRunningOrder(RunningOrder order) => throw new NotImplementedException();
    public void DeleteRunningOrder(Guid id) => throw new NotImplementedException();
    public RunningOrder? FindRunningOrder(Guid id) => null;

    public void ReplaceState(AppState state) => _state = state;
    public AppState Snapshot() => _state;
}
