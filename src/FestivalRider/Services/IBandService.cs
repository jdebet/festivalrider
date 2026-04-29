using FestivalRider.Models;

namespace FestivalRider.Services;

public interface IBandService
{
    IReadOnlyList<Band> Bands { get; }
    IReadOnlyList<RunningOrder> RunningOrders { get; }
    IReadOnlyList<ShowData> Shows { get; }
    Guid ActiveShowId { get; }

    event Action? OnChange;

    void AddBand(Band band);
    void UpdateBand(Band band);
    void DeleteBand(Guid id);
    Band? FindBand(Guid id);

    void AddRunningOrder(RunningOrder order);
    void UpdateRunningOrder(RunningOrder order);
    void DeleteRunningOrder(Guid id);
    RunningOrder? FindRunningOrder(Guid id);
    IEnumerable<RunningOrder> RunningOrdersForActiveShow { get; }

    int AddStage(string name);
    int AddStage(Guid showId, string name);
    void UpdateStage(Stage stage);
    void UpdateStage(Guid showId, Stage stage);
    void DeleteStage(int id);
    void DeleteStage(Guid showId, int id);
    Stage? FindStage(int id);
    Stage? FindStage(Guid showId, int id);
    ShowData? FindShow(Guid id);

    Guid AddShow(string name);
    Task UpdateShow(ShowData show);
    Task DeleteShow(Guid id);
    Task SetActiveShow(Guid id);

    void ReplaceState(AppState state);
    AppState Snapshot();
}
