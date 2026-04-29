using FestivalRider.PrintStrategies;

namespace FestivalRider.Services;

public interface IPdfExportService
{
    Task PrintAsync(IPrintStrategy strategy, object context);
    Task<byte[]?> RenderToPdfAsync(IPrintStrategy strategy, object context);
}
