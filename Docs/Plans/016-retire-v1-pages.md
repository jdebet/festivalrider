# 016 — Retire v1 pages

## Status

Active

## Context

Successor to 015 (UI density). The v2 pages (`/bands-v2`, `/running-order-v2`, `/band-v2/{Id:guid}`) have been built and tested in parallel. This plan retires the old v1 pages so the v2 UI becomes the only UI. The model refactor planned as 016 is now 017.

## Decisions (locked)

- **v2 becomes primary** — v2 routes drop the `-v2` suffix and replace the old routes exactly.
- **v1 files are deleted** — `Pages/BandList.razor`, `Pages/RunningOrder.razor`, `Pages/RiderEditor.razor` are removed. Their components (`BandCard.razor`, `RiderSection.razor` if unused) are removed.
- **Nav menu simplified** — only primary routes remain; no dual nav items.
- **BandListV2 edit route fixed** — already navigates to `/band-v2/{id}`; after this plan it navigates to `/band/{id}`.

## Architecture rules

- Old v1 page files MUST be deleted, not left as dead code.
- v2 page `@page` directives MUST be updated to the primary routes (`/`, `/running-order`, `/band/{Id:guid}`).
- `NavMenu.razor` MUST show only the primary routes.
- Any v1-only components (e.g. `BandCard.razor`) MUST be deleted if no longer referenced.
- `RiderSection.razor` MUST be kept; it is still used by v2 pages.
- Backward links in `BandListV2` and `RiderEditorV2` MUST be updated from `/bands-v2` to `/`.

## File-by-file scope

### Delete

- `Pages/BandList.razor` — old card-grid band list.
- `Pages/RunningOrder.razor` — old running order page.
- `Pages/RiderEditor.razor` — old rider editor.
- `Components/BandCard.razor` — old card component; only used by v1 band list.

### Rename / update route

- `Pages/BandListV2.razor` — change `@page "/bands-v2"` to `@page "/"`.
- `Pages/RunningOrderV2.razor` — change `@page "/running-order-v2"` to `@page "/running-order"`.
- `Pages/RiderEditorV2.razor` — change `@page "/band-v2/{Id:guid}"` to `@page "/band/{Id:guid}"`.

### Update references

- `NavMenu.razor` — remove `-v2` suffix from nav links; keep only Bands, Running Order, Settings.
- `BandListV2.razor` — `Nav.NavigateTo("/")` stays correct (already `/` for back).
- `RiderEditorV2.razor` — `GoBack()` navigates to `/` (already correct).

## Task order

1. Delete v1 pages and `BandCard.razor`.
2. Update v2 page route directives to primary routes.
3. Update `NavMenu.razor` to remove v2 suffixes.
4. Verify build and all remaining tests pass.
5. Update plan 015 status to `Superseded by 016` in `readme.md`.

## Implementation cadence

- **Wave 1 — Delete and reroute**: remove v1 files, update v2 routes, simplify nav.
- **Wave 2 — Verify**: build, test, check no broken links.

## Out of scope

- Any model changes (now plan 017).
- Print routes and strategies.
- Settings page.

## Risks & migrations

- **Risk**: deep links to old routes (`/band/{id}` from external bookmarks) break until wave 1 reroutes the v2 page to that path. **Mitigation**: do route updates and deletions in the same commit so there is no window of brokenness.
- **Risk**: `BandListV2.AddBand` navigates to `/band-v2/{id}` which will 404 after the route change. **Mitigation**: fix the navigate string in the same commit as the route directive change.
