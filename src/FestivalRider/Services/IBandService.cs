using FestivalRider.Models;

namespace FestivalRider.Services;

public interface IBandService
{
    IReadOnlyList<Band> Bands { get; }
    IReadOnlyList<RunningOrder> RunningOrders { get; }

    event Action? OnChange;

    void AddBand(Band band);
    void UpdateBand(Band band);
    void DeleteBand(Guid id);
    Band? FindBand(Guid id);

    void AddRunningOrder(RunningOrder order);
    void UpdateRunningOrder(RunningOrder order);
    void DeleteRunningOrder(Guid id);
    RunningOrder? FindRunningOrder(Guid id);

    int AddStage(string name);
    void UpdateStage(Stage stage);
    void DeleteStage(int id);
    Stage? FindStage(int id);

    void ReplaceState(AppState state);
    AppState Snapshot();
}
