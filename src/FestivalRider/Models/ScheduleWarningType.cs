namespace FestivalRider.Models;

public enum ScheduleWarningType
{
    BreakTimeViolation,
    SoundcheckBlockOverlap,
    OnStageOverlap,
    BackwardLockConflict,
    BarrierConflict,
    CateringOutsideHours,
    CurfewViolation,
    SoundcheckShrunk,
    SoundcheckOrderOverlap,
    UserOverrideOverlap,
    EarlySoundcheckAfterOnStage,
    ConstraintViolation,
    FirstShowTimeMissing,
    VenueClosed,
}
