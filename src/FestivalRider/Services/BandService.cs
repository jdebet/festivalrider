using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

// Sync API: mutations are pure in-memory operations on AppState; no I/O or awaitable work.
public class BandService : IBandService
{
    private readonly ILogger<BandService> _logger;
    private AppState _state = new();
    private int _nextStageId = 1;

    public BandService(ILogger<BandService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<Band> Bands => _state.Bands;
    public IReadOnlyList<RunningOrder> RunningOrders => _state.RunningOrders;

    public event Action? OnChange;

    public void AddBand(Band band)
    {
        if (band is null) throw new ArgumentNullException(nameof(band));
        if (_state.Bands.Any(b => b.Id == band.Id))
            throw new InvalidOperationException($"Band with id {band.Id} already exists.");
        var now = DateTimeOffset.UtcNow;
        band.CreatedAt = now;
        band.UpdatedAt = now;
        _state.Bands.Add(band);
        _logger.LogInformation("Added band {Id} {Name}", band.Id, band.Name);
        Raise();
    }

    public void UpdateBand(Band band)
    {
        if (band is null) throw new ArgumentNullException(nameof(band));
        var index = _state.Bands.FindIndex(b => b.Id == band.Id);
        if (index < 0) throw new InvalidOperationException($"Band {band.Id} not found.");
        band.UpdatedAt = DateTimeOffset.UtcNow;
        _state.Bands[index] = band;
        Raise();
    }

    public void DeleteBand(Guid id)
    {
        var removed = _state.Bands.RemoveAll(b => b.Id == id);
        if (removed == 0) return;
        // Drop slots referencing the deleted band so running orders stay consistent.
        foreach (var ro in _state.RunningOrders)
            ro.Slots.RemoveAll(s => s.BandId == id);
        Raise();
    }

    public Band? FindBand(Guid id) => _state.Bands.FirstOrDefault(b => b.Id == id);

    public void AddRunningOrder(RunningOrder order)
    {
        if (order is null) throw new ArgumentNullException(nameof(order));
        if (_state.RunningOrders.Any(o => o.Id == order.Id))
            throw new InvalidOperationException($"RunningOrder {order.Id} already exists.");
        _state.RunningOrders.Add(order);
        Raise();
    }

    public void UpdateRunningOrder(RunningOrder order)
    {
        if (order is null) throw new ArgumentNullException(nameof(order));
        var index = _state.RunningOrders.FindIndex(o => o.Id == order.Id);
        if (index < 0) throw new InvalidOperationException($"RunningOrder {order.Id} not found.");
        _state.RunningOrders[index] = order;
        Raise();
    }

    public void DeleteRunningOrder(Guid id)
    {
        if (_state.RunningOrders.RemoveAll(o => o.Id == id) > 0) Raise();
    }

    public RunningOrder? FindRunningOrder(Guid id) => _state.RunningOrders.FirstOrDefault(o => o.Id == id);

    public int AddStage(string name)
    {
        var stage = new Stage { Id = _nextStageId++, Name = name ?? string.Empty };
        _state.ShowData.Stages.Add(stage);
        Raise();
        return stage.Id;
    }

    public void UpdateStage(Stage stage)
    {
        if (stage is null) throw new ArgumentNullException(nameof(stage));
        var index = _state.ShowData.Stages.FindIndex(s => s.Id == stage.Id);
        if (index < 0) throw new InvalidOperationException($"Stage {stage.Id} not found.");
        _state.ShowData.Stages[index] = stage;
        Raise();
    }

    public void DeleteStage(int id)
    {
        // Deleted stage IDs are not reused; orphaned slots surface as "Unknown stage".
        if (_state.ShowData.Stages.RemoveAll(s => s.Id == id) > 0) Raise();
    }

    public Stage? FindStage(int id) => _state.ShowData.Stages.FirstOrDefault(s => s.Id == id);

    public void ReplaceState(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        var maxId = _state.ShowData.Stages.Count == 0 ? 0 : _state.ShowData.Stages.Max(s => s.Id);
        _nextStageId = maxId + 1;
        Raise();
    }

    public AppState Snapshot() => _state;

    private void Raise() => OnChange?.Invoke();
}
