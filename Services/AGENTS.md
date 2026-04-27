# Services — Agent rules

- `BandService` MUST be the sole mutator of `AppState`. Other services MUST NOT mutate state directly.
- Every `BandService` mutation MUST set `Band.UpdatedAt = DateTimeOffset.UtcNow` then raise `OnChange`.
- `BandService` MUST throw on duplicate `Guid Id` add.
- USE `Snapshot()` and `ReplaceState(AppState)` as the only import/export round-trip surfaces.
- `IStorageService.EnsureLoadedAsync` MUST be idempotent.
- `IPdfExportService.PrintAsync` MUST call `print.js` `triggerPrint()` only after the target page reports ready.
