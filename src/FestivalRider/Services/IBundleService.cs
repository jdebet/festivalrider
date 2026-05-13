using FestivalRider.Models;

namespace FestivalRider.Services;

public interface IBundleService
{
    byte[] ExportBundle(ShowData show);
    BundleImportResult ImportBundle(Stream zip, Guid targetShowId, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null);

    byte[] ExportMasterBundle(AppState state);
    BundleImportResult ImportMasterBundle(Stream zip, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null);
}
