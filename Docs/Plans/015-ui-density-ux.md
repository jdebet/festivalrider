# 015 — UI density and UX rework

## Status

Superseded by 016

## Context

Users report the current UI feels clumsy and wastes space. Specific pain points:

- **Layout**: the Blazor-template sidebar burns ~250 px horizontally for 3 nav links; the `top-row` "About" link is dead weight.
- **Band list**: 3-column card grid with `h-100` cards forces equal heights and low information density; no filter/search.
- **Running order**: generous table cell padding, metadata row above every day table, scattered export controls, no slot reordering.
- **Rider editor**: nested `border rounded p-2` wrappers, wide single-column layouts for collapsed sections.

This plan adds parallel "v2" pages so the team can A/B test against the originals before retiring them.

## Decisions (locked)

- **Parallel routes** — every reworked UI ships under a new route (`/bands-v2`, `/running-order-v2`, `/band-v2/{Id:guid}`) so old and new can coexist. Retirement happens in a successor plan, never here.
- **Top navbar** — the global layout collapses the sidebar into a horizontal top navbar. This is a global change, not page-specific.
- **No new NuGet packages** — all visual changes use Bootstrap 5 utilities and hand-rolled CSS. Reorder drag uses native HTML5 DnD.
- **Components stay pure** — new components live under `Components/`; they do not inject services. Pages orchestrate and pass data via `[Parameter]`.

## Architecture rules

- Old routes (`/`, `/running-order`, `/band/{Id:guid}`) MUST NOT be modified or removed during this plan.
- New pages MUST follow existing agent rules (orchestrate in pages, no service injection in components, `EditForm` + `DataAnnotationsValidator`, etc.).
- New routes MUST register in `NavMenu` alongside existing ones so switching is one click.
- All user-facing strings MUST resolve through `ILocalizationService.T(key, args)` and be added to `en.json` + `LocalizationKeys`.

## File-by-file scope

### Layout

- `Layout/MainLayout.razor` — replace sidebar with top navbar; remove dead `top-row`; tighten article padding.
- `Layout/NavMenu.razor` — add nav links for `/bands-v2`, `/running-order-v2`; keep existing links.

### Pages (new)

- `Pages/BandListV2.razor` — dense band table with live filter, icon actions. Route `/bands-v2`.
- `Pages/RunningOrderV2.razor` — compact slot table, consolidated toolbar, HTML5 drag-to-reorder. Route `/running-order-v2`.
- `Pages/RiderEditorV2.razor` — tighter forms, 2-column tech sections on `lg+`. Route `/band-v2/{Id:guid}`.

### Components (new)

- `Components/BandTableRow.razor` — single row for the dense band table. `[Parameter] Band`, `[Parameter] EventCallback<Guid>` for edit/delete/print.
- `Components/RunningOrderSlotRow.razor` — single draggable slot row. `[Parameter] RunningOrderSlot`, `[Parameter] EventCallback<DragEventArgs>` drag handles.
- `Components/CompactToolbar.razor` — reusable icon-button + dropdown toolbar for export/print actions.

### Styles

- `wwwroot/css/app.css` — add density utility classes (`.table-dense`, `.form-ultra-sm`, `.py-tight`).

## Task order

1. Tighten global layout: top navbar, remove `top-row`, reduce `content` padding.
2. Add density CSS utilities.
3. Build `BandListV2` + `BandTableRow`: dense table, live filter, icon actions. Register route `/bands-v2` and nav item.
4. Add filter localization keys + `LocalizationKeys` constants.
5. Build `RunningOrderV2` + `RunningOrderSlotRow` + `CompactToolbar`: compact table, toolbar consolidation, drag reorder. Register route `/running-order-v2` and nav item.
6. Add running-order v2 localization keys + `LocalizationKeys` constants.
7. Build `RiderEditorV2`: tighter forms, 2-column sections, reduced card nesting. Register route `/band-v2/{Id:guid}` and nav item.
8. Add rider editor v2 localization keys + `LocalizationKeys` constants.
9. Verify all new pages compile and old pages remain untouched.

## Implementation cadence

- **Wave 1 — Global layout + Band list v2**: navbar refactor, density CSS, dense band table with filter.
- **Wave 2 — Running order v2**: compact slot table, consolidated toolbar, drag-to-reorder.
- **Wave 3 — Rider editor v2**: tighter forms, 2-column tech sections.
- **Wave 4 — Polish**: responsive testing, localization parity, CSS cleanup.

## Out of scope

- Retiring old routes/pages (reserved for plan 016).
- Any changes to print routes or print layouts.
- Data model changes; only presentation changes.

## Risks & migrations

- **Risk**: top navbar on mobile may wrap or overflow. **Mitigation**: use Bootstrap `navbar-expand-lg` with a hamburger; test at 375 px width.
- **Risk**: drag-and-reorder may conflict with existing click handlers. **Mitigation**: use `draggable` on a dedicated handle element, not the full row.
- **Risk**: new CSS classes may clash with Bootstrap defaults. **Mitigation**: prefix all custom density classes with `fr-` (e.g. `.fr-table-dense`).
