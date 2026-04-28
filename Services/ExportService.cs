using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

public class ExportService : IExportService
{
    private readonly ILogger<ExportService> _logger;

    public ExportService(ILogger<ExportService> logger)
    {
        _logger = logger;
    }

    public string ExportBandCsv(Band band) => throw new NotImplementedException();
    public Band ImportBandCsv(string csv) => throw new NotImplementedException();
    public string ExportRunningOrderCsv(RunningOrder order) => throw new NotImplementedException();
    public string ExportRunningOrderByStageCsv(RunningOrder order, string stage) => throw new NotImplementedException();
    public string ExportRunningOrderByBandCsv(RunningOrder order, Guid bandId) => throw new NotImplementedException();
}
