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

    public IReadOnlyList<Band> Bands => ActiveShow?.Bands ?? new List<Band>();
    public IReadOnlyList<RunningOrder> RunningOrders => ActiveShow?.RunningOrders ?? new List<RunningOrder>();
    public IReadOnlyList<ShowData> Shows => _state.Shows;
    public Guid ActiveShowId => _state.ActiveShowId;

    private ShowData? ActiveShow => FindShow(_state.ActiveShowId);

    public event Action? OnChange;

    public void AddBand(Band band)
    {
        if (band is null) throw new ArgumentNullException(nameof(band));
        var show = ActiveShow ?? throw new InvalidOperationException("No active show.");
        if (show.Bands.Any(b => b.Id == band.Id))
            throw new InvalidOperationException($"Band with id {band.Id} already exists.");
        var now = DateTimeOffset.UtcNow;
        band.CreatedAt = now;
        band.UpdatedAt = now;
        show.Bands.Add(band);
        _logger.LogInformation("Added band {Id} {Name} to show {ShowId}", band.Id, band.Name, show.Id);
        Raise();
    }

    public void UpdateBand(Band band)
    {
        if (band is null) throw new ArgumentNullException(nameof(band));
        var show = ActiveShow ?? throw new InvalidOperationException("No active show.");
        var index = show.Bands.FindIndex(b => b.Id == band.Id);
        if (index < 0) throw new InvalidOperationException($"Band {band.Id} not found in active show.");
        band.UpdatedAt = DateTimeOffset.UtcNow;
        show.Bands[index] = band;
        Raise();
    }

    public void DeleteBand(Guid id)
    {
        var show = ActiveShow;
        if (show is null) return;
        var removed = show.Bands.RemoveAll(b => b.Id == id);
        if (removed == 0) return;
        // Drop slots referencing the deleted band so running orders stay consistent.
        foreach (var ro in show.RunningOrders)
            ro.Slots.RemoveAll(s => s.BandId == id);
        Raise();
    }

    public Band? FindBand(Guid id) =>
        _state.Shows.SelectMany(s => s.Bands).FirstOrDefault(b => b.Id == id);

    public Band? FindBand(Guid showId, Guid id) =>
        FindShow(showId)?.Bands.FirstOrDefault(b => b.Id == id);

    public void AddRunningOrder(RunningOrder order)
    {
        if (order is null) throw new ArgumentNullException(nameof(order));
        var show = ActiveShow ?? throw new InvalidOperationException("No active show.");
        if (show.RunningOrders.Any(o => o.Id == order.Id))
            throw new InvalidOperationException($"RunningOrder {order.Id} already exists.");
        if (order.ShowId == Guid.Empty) order.ShowId = _state.ActiveShowId;
        show.RunningOrders.Add(order);
        Raise();
    }

    public void UpdateRunningOrder(RunningOrder order)
    {
        if (order is null) throw new ArgumentNullException(nameof(order));
        var show = ActiveShow ?? throw new InvalidOperationException("No active show.");
        var index = show.RunningOrders.FindIndex(o => o.Id == order.Id);
        if (index < 0) throw new InvalidOperationException($"RunningOrder {order.Id} not found in active show.");
        var prior = show.RunningOrders[index];
        var dayDelta = order.ShowDayNumber - prior.ShowDayNumber;
        if (dayDelta != 0)
        {
            ShiftRunningOrderTimeSlots(order, dayDelta);
        }
        show.RunningOrders[index] = order;
        Raise();
    }

    private static void ShiftRunningOrderTimeSlots(RunningOrder order, int dayDelta)
    {
        ShiftSlot(order.BreakfastHoursOverride, dayDelta);
        ShiftSlot(order.LunchHoursOverride, dayDelta);
        ShiftSlot(order.DinnerHoursOverride, dayDelta);
        foreach (var slot in order.Slots)
            ShiftSlot(slot.CateringSlot, dayDelta);
    }

    private static void ShiftSlot(TimeSlot? ts, int dayDelta)
    {
        if (ts is null) return;
        ts.Start = ts.Start.AddDays(dayDelta);
        if (ts.End is DateTime e) ts.End = e.AddDays(dayDelta);
    }

    public void DeleteRunningOrder(Guid id)
    {
        var show = ActiveShow;
        if (show is null) return;
        if (show.RunningOrders.RemoveAll(o => o.Id == id) > 0) Raise();
    }

    public RunningOrder? FindRunningOrder(Guid id) =>
        _state.Shows.SelectMany(s => s.RunningOrders).FirstOrDefault(o => o.Id == id);

    public RunningOrder? FindRunningOrder(Guid showId, Guid id) =>
        FindShow(showId)?.RunningOrders.FirstOrDefault(o => o.Id == id);

    public IEnumerable<RunningOrder> RunningOrdersForActiveShow =>
        ActiveShow?.RunningOrders ?? Enumerable.Empty<RunningOrder>();

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
        var removed = show.Stages.RemoveAll(s => s.Id == id);
        if (removed == 0) return;
        // Prune orphan ids from StageLinkGroups; drop any group left with fewer than 2 ids.
        foreach (var group in show.StageLinkGroups)
            group.StageIds.RemoveAll(sid => sid == id);
        show.StageLinkGroups.RemoveAll(g => g.StageIds.Count < 2);
        Raise();
    }

    public Stage? FindStage(int id) => FindStage(_state.ActiveShowId, id);

    public Stage? FindStage(Guid showId, int id)
    {
        var show = FindShow(showId);
        if (show is null) return null;
        if (show.Stages.Count == 0 && id == 0)
            return new Stage { Id = 0, Name = string.Empty };
        return show.Stages.FirstOrDefault(s => s.Id == id);
    }

    public ShowData? FindShow(Guid id) => _state.Shows.FirstOrDefault(s => s.Id == id);

    // ---- Show CRUD ----

    public Guid AddShow(string name)
    {
        var show = new ShowData
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled show" : name,
            Bands = new(),
            RunningOrders = new(),
        };
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
        var existing = _state.Shows[index];
        var priorDate = existing.DateOfOpening;
        existing.Name = show.Name;
        existing.Address = show.Address;
        existing.DateOfOpening = show.DateOfOpening;
        existing.ShowDayCount = show.ShowDayCount;
        existing.Stages = show.Stages;
        existing.DefaultScheduleMode = show.DefaultScheduleMode;
        existing.DefaultAnchorEvent = show.DefaultAnchorEvent;
        existing.VenueOpenTime = show.VenueOpenTime;
        existing.VenueCloseTime = show.VenueCloseTime;
        existing.TechnicalGetInTime = show.TechnicalGetInTime;
        existing.DoorsOpeningTime = show.DoorsOpeningTime;
        existing.FirstShowTime = show.FirstShowTime;
        existing.SoundCurfewTime = show.SoundCurfewTime;
        existing.BackstageCurfewTime = show.BackstageCurfewTime;
        existing.BreakfastHours = show.BreakfastHours;
        existing.LunchHours = show.LunchHours;
        existing.DinnerHours = show.DinnerHours;
        existing.BreakTimeMinutes = show.BreakTimeMinutes;
        existing.SoundcheckGapMinutes = show.SoundcheckGapMinutes;
        existing.StageLinkGroups = show.StageLinkGroups;
        // Preserve existing Bands and RunningOrders lists.
        if (priorDate != default && show.DateOfOpening != default && priorDate != show.DateOfOpening)
        {
            var dayDelta = (show.DateOfOpening.ToDateTime(TimeOnly.MinValue) - priorDate.ToDateTime(TimeOnly.MinValue)).Days;
            ShiftSlot(existing.BreakfastHours, dayDelta);
            ShiftSlot(existing.LunchHours, dayDelta);
            ShiftSlot(existing.DinnerHours, dayDelta);
            foreach (var ro in existing.RunningOrders)
                ShiftRunningOrderTimeSlots(ro, dayDelta);
        }
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
            var seed = new ShowData { Name = "Untitled show", Bands = new(), RunningOrders = new() };
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
            var seed = new ShowData { Name = "Untitled show", Bands = new(), RunningOrders = new() };
            state.Shows.Add(seed);
            state.ActiveShowId = seed.Id;
            return;
        }
        foreach (var show in state.Shows)
        {
            show.Bands ??= new List<Band>();
            show.RunningOrders ??= new List<RunningOrder>();
        }
        if (state.Shows.All(s => s.Id != state.ActiveShowId))
            state.ActiveShowId = state.Shows[0].Id;
    }

    private void Raise() => OnChange?.Invoke();
}
