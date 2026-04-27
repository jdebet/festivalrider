# Models — Agent rules

- USE auto-property defaults for required initial state (`= new()`, `= Guid.NewGuid()`, `= DateTimeOffset.UtcNow`).
- APPLY `[Required]` to `Band.Name` and `RunningOrder.FestivalName`; `[EmailAddress]` to `Contact.Email`.
- Aggregate roots (`Band`, `RunningOrder`) MUST expose `Guid Id` defaulted to `Guid.NewGuid()`.
- NEVER set `UpdatedAt` inside a model; `BandService` owns that write.
