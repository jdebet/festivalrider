namespace FestivalRider.Models;

[Flags]
public enum UserOverrideFlags
{
    None = 0,
    AllowSoundcheckOverlap = 1,
    AllowOnStageOverlap = 2,
}
