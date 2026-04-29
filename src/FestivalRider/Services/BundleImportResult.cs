using FestivalRider.Models;

namespace FestivalRider.Services;

public record BundleImportResult(
    AppState? State,
    int BandCount,
    int RunningOrderCount,
    IReadOnlyList<string> Warnings,
    string? Error);

