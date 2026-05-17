namespace FestivalRider.Models;

public class FestivalTimingTemplate
{
    public List<TimingChainEntry> EarlyChain { get; set; } = new();
    public List<TimingChainEntry> PreShowEntries { get; set; } = new();
    public List<TimingChainEntry> PostShowEntries { get; set; } = new();
    public int DefaultSetLengthMinutes { get; set; } = 60;
}
