# Pages — Agent rules

- Route map (locked):
  - `/` → `BandListV2.razor`.
  - `/band/{Id:guid}` → `RiderEditorV2.razor`.
  - `/running-order` → `RunningOrderV2.razor`.
  - `/print/{StrategyKey}/{ContextId}` → `RiderPrint.razor` with `@layout EmptyLayout`.
  - `/settings` → `Settings.razor`.
- Pages consuming `BandService` state MUST subscribe to `OnChange` in `OnInitialized[Async]`, implement `IDisposable`, and unsubscribe in `Dispose`.
- `OnChange` handlers MUST call `InvokeAsync(StateHasChanged)`.
- `RiderPrint.razor` MUST render a 404 panel on missing entity and NEVER throw.
- Destructive actions MUST confirm via `ConfirmDialog`.
- `Settings.razor` MUST invoke `IStorageService.ClearAsync` / `FlushAsync` for "Clear all data" / "Force save".
