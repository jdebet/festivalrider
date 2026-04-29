# Print strategies — Agent rules

- Implement `IPrintStrategy` with `string Key`, `string GetTitle(object context)`, `RenderFragment Render(object context)`.
- `Key` MUST be lowercase, URL-safe, and unique across all registered strategies.
- Strategies MUST cast `context` to their declared type and throw on mismatch.
- Strategies MAY receive services via constructor DI (e.g., `IBandService`). NEVER inject `IJSRuntime`.
- Locked strategies:
  - `BandRiderPrintStrategy`: `Key = "band"`, context `Guid` (band id).
  - `StagePrintStrategy`: `Key = "stage"`, context `record StageContext(Guid RunningOrderId, int StageId)`.
  - `RolePrintStrategy`: `Key = "role"`, context `record RoleContext(Guid RunningOrderId, ContactRole Role)`.
- ALL print styles MUST live in `wwwroot/css/print.css` under `@media print`: hide nav/buttons, apply per-section page breaks, keep tables monochrome-friendly.
