using FestivalRider.Models;

namespace FestivalRider.Services;

public interface IExportService
{
    string ExportBandCsv(Band band);
    Band ImportBandCsv(string csv);
    string ExportRunningOrderCsv(RunningOrder order);
    string ExportRunningOrderByStageCsv(RunningOrder order, string stage);
    string ExportRunningOrderByBandCsv(RunningOrder order, Guid bandId);
}
