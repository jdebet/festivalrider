using FestivalRider.PrintStrategies;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FestivalRider.Services;

public class PdfExportService : IPdfExportService
{
    private readonly IJSRuntime _js;
    private readonly ILogger<PdfExportService> _logger;

    public PdfExportService(IJSRuntime js, ILogger<PdfExportService> logger)
    {
        _js = js;
        _logger = logger;
    }

    // Caller is responsible for navigating to /print/{strategy.Key}/{contextId} and awaiting render
    // before invoking PrintAsync (e.g., RiderPrint.razor wires this to a visible "Print" button).
    public async Task PrintAsync(IPrintStrategy strategy, object context)
    {
        await _js.InvokeVoidAsync("festivalRiderPrint.triggerPrint");
    }

    public Task<byte[]?> RenderToPdfAsync(IPrintStrategy strategy, object context)
    {
        _logger.LogWarning("RenderToPdfAsync is not implemented; returning null.");
        // SWAP: jsPDF implementation goes here
        return Task.FromResult<byte[]?>(null);
    }
}
