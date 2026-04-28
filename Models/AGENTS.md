# Models — Agent rules

- USE auto-property defaults for required initial state (`= new()`, `= Guid.NewGuid()`, `= DateTimeOffset.UtcNow`).
- APPLY `[Required]` to `Band.Name`, `ShowData.Name`, `Stage.Name`, `Party.Name`; `[EmailAddress]` to `Contact.Email`; `[Range(1, 31)]` to `ShowData.ShowDayCount` and `RunningOrder.ShowDayNumber`.
- Aggregate roots (`Band`, `RunningOrder`) MUST expose `Guid Id` defaulted to `Guid.NewGuid()`. `Stage.Id` is `int` assigned by `IBandService.AddStage`; never user-edited.
- NEVER set `UpdatedAt` inside a model; `BandService` owns that write.
