using FestivalRider.Models;

namespace FestivalRider.Services;

public interface IBundleService
{
    byte[] ExportBundle(AppState state);
    BundleImportResult ImportBundle(Stream zip, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null);
}
