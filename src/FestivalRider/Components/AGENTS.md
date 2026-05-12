# Components — Agent rules

- MAY hold transient UI state (form buffers, `_expanded`, `_hover`) as private fields.
- NEVER inject `IJSRuntime`, `HttpClient`, or any storage service.
- Filename MUST match the component type name.
- Locked parameter surfaces:
  - `RiderSection.razor`: `[Parameter] string Title`, `[Parameter] RenderFragment ChildContent`.
