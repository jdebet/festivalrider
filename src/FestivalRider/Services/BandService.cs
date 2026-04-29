using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

// Sync API: mutations are pure in-memory operations on AppState; no I/O or awaitable work.
// Show CRUD methods return Task per plan 006 so callers can await uniformly even though work is sync.
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
    public IReadOnlyList<ShowData> Shows => _state.Shows;
    public Guid ActiveShowId => _state.ActiveShowId;

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
        if (order.ShowId == Guid.Empty) order.ShowId = _state.ActiveShowId;
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

    public IEnumerable<RunningOrder> RunningOrdersForActiveShow =>
        _state.RunningOrders.Where(o => o.ShowId == _state.ActiveShowId);

    // ---- Stage CRUD (per-show; default targets the active show) ----

    public int AddStage(string name) => AddStage(_state.ActiveShowId, name);

    public int AddStage(Guid showId, string name)
    {
        var show = RequireShow(showId);
        var nextId = show.Stages.Count == 0 ? 1 : show.Stages.Max(s => s.Id) + 1;
        show.Stages.Add(new Stage { Id = nextId, Name = name ?? string.Empty });
        Raise();
        return nextId;
    }

    public void UpdateStage(Stage stage) => UpdateStage(_state.ActiveShowId, stage);

    public void UpdateStage(Guid showId, Stage stage)
    {
        if (stage is null) throw new ArgumentNullException(nameof(stage));
        var show = RequireShow(showId);
        var index = show.Stages.FindIndex(s => s.Id == stage.Id);
        if (index < 0) throw new InvalidOperationException($"Stage {stage.Id} not found in show {showId}.");
        show.Stages[index] = stage;
        Raise();
    }

    public void DeleteStage(int id) => DeleteStage(_state.ActiveShowId, id);

    public void DeleteStage(Guid showId, int id)
    {
        var show = RequireShow(showId);
        // Deleted stage IDs are not reused within a show; orphaned slots surface as "Unknown stage".
        if (show.Stages.RemoveAll(s => s.Id == id) > 0) Raise();
    }

    public Stage? FindStage(int id) => FindStage(_state.ActiveShowId, id);

    public Stage? FindStage(Guid showId, int id) =>
        FindShow(showId)?.Stages.FirstOrDefault(s => s.Id == id);

    public ShowData? FindShow(Guid id) => _state.Shows.FirstOrDefault(s => s.Id == id);

    // ---- Show CRUD ----

    public Guid AddShow(string name)
    {
        var show = new ShowData { Name = string.IsNullOrWhiteSpace(name) ? "Untitled show" : name };
        _state.Shows.Add(show);
        _logger.LogInformation("Added show {Id} {Name}", show.Id, show.Name);
        Raise();
        return show.Id;
    }

    public Task UpdateShow(ShowData show)
    {
        if (show is null) throw new ArgumentNullException(nameof(show));
        var index = _state.Shows.FindIndex(s => s.Id == show.Id);
        if (index < 0) throw new InvalidOperationException($"Show {show.Id} not found.");
        _state.Shows[index] = show;
        Raise();
        return Task.CompletedTask;
    }

    public Task DeleteShow(Guid id)
    {
        var index = _state.Shows.FindIndex(s => s.Id == id);
        if (index < 0) return Task.CompletedTask;
        _state.Shows.RemoveAt(index);

        if (_state.Shows.Count == 0)
        {
            // Empty list is invalid — seed a fresh "Untitled show".
            var seed = new ShowData { Name = "Untitled show" };
            _state.Shows.Add(seed);
            _state.ActiveShowId = seed.Id;
        }
        else if (_state.ActiveShowId == id)
        {
            // Flip to next-by-creation-order survivor (we use list order as proxy).
            _state.ActiveShowId = _state.Shows[0].Id;
        }
        Raise();
        return Task.CompletedTask;
    }

    public Task SetActiveShow(Guid id)
    {
        if (_state.Shows.All(s => s.Id != id))
            throw new InvalidOperationException($"Show {id} not found.");
        if (_state.ActiveShowId != id)
        {
            _state.ActiveShowId = id;
            Raise();
        }
        return Task.CompletedTask;
    }

    public void ReplaceState(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        EnsureShowInvariants(_state);
        Raise();
    }

    public AppState Snapshot() => _state;

    private ShowData RequireShow(Guid id) =>
        FindShow(id) ?? throw new InvalidOperationException($"Show {id} not found.");

    private static void EnsureShowInvariants(AppState state)
    {
        if (state.Shows.Count == 0)
        {
            var seed = new ShowData { Name = "Untitled show" };
            state.Shows.Add(seed);
            state.ActiveShowId = seed.Id;
            return;
        }
        if (state.Shows.All(s => s.Id != state.ActiveShowId))
            state.ActiveShowId = state.Shows[0].Id;
    }

    private void Raise() => OnChange?.Invoke();
}
