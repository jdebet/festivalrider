using FestivalRider.Models;

namespace FestivalRider.Services;

public interface IExportService
{
    string ExportBandCsv(Band band);
    Band ImportBandCsv(string csv);

    string ExportShowCsv(ShowData show);
    ShowData ImportShowCsv(string csv);

    string ExportRunningOrderCsv(RunningOrder order);
    string ExportRunningOrderByStageCsv(RunningOrder order, int stageId);
    string ExportRunningOrderByBandCsv(RunningOrder order, Guid bandId);
}
