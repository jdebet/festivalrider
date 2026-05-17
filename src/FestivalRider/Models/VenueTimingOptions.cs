namespace FestivalRider.Models;

public class VenueTimingOptions
{
    public bool IncludeGetIn { get; set; } = true;
    public bool IncludeLoadInVenue { get; set; } = false;
    public bool IncludeStageLoadIn { get; set; } = true;
    public bool IncludeBackstageDrop { get; set; } = false;
    public bool IncludeSetupOnStage { get; set; } = true;
    public bool IncludeSoundcheck { get; set; } = true;
    public bool IncludePreShowLinecheck { get; set; } = true;

    public int DefaultGetInMinutes { get; set; } = 15;
    public int DefaultLoadInVenueMinutes { get; set; } = 30;
    public int DefaultStageLoadInMinutes { get; set; } = 30;
    public int DefaultBackstageDropMinutes { get; set; } = 15;
    public int DefaultSetupOnStageMinutes { get; set; } = 15;
    public int DefaultSoundcheckMinutes { get; set; } = 30;
    public int DefaultPreShowLinecheckMinutes { get; set; } = 10;
    public int DefaultBackstageLeadMinutes { get; set; } = 15;
    public int DefaultChangeoverMinutes { get; set; } = 15;
    public int DefaultSetLengthMinutes { get; set; } = 45;
}
