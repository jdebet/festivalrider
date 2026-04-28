using FestivalRider.PrintStrategies;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

public class PdfExportService : IPdfExportService
{
    private readonly ILogger<PdfExportService> _logger;

    public PdfExportService(ILogger<PdfExportService> logger)
    {
        _logger = logger;
    }

    public Task PrintAsync(IPrintStrategy strategy, object context) => Task.CompletedTask;

    public Task<byte[]?> RenderToPdfAsync(IPrintStrategy strategy, object context)
    {
        _logger.LogWarning("RenderToPdfAsync is not implemented; returning null.");
        // SWAP: jsPDF implementation goes here
        return Task.FromResult<byte[]?>(null);
    }
}
