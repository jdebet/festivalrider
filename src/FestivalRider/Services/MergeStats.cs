namespace FestivalRider.Services;

public record MergeStats(
    int BandsAdded,
    int BandsUpdated,
    int BandsSkipped,
    int RunningOrdersAdded,
    int RunningOrdersUpdated,
    int RunningOrdersSkipped);
