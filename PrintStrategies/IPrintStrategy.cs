using Microsoft.AspNetCore.Components;

namespace FestivalRider.PrintStrategies;

public interface IPrintStrategy
{
    string Key { get; }
    string GetTitle(object context);
    RenderFragment Render(object context);
}
