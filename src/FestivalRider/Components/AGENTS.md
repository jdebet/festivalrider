# Components — Agent rules

- MAY hold transient UI state (form buffers, `_expanded`, `_hover`) as private fields.
- NEVER inject `IJSRuntime`, `HttpClient`, or any storage service.
- Filename MUST match the component type name.
- Locked parameter surfaces:
  - `BandCard.razor`: `[Parameter] Band Band`; `[Parameter] EventCallback<Guid> OnEdit / OnDelete / OnPrint`.
  - `RiderSection.razor`: `[Parameter] string Title`, `[Parameter] RenderFragment ChildContent`.
