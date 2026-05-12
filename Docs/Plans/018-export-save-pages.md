# 018 — Export and Save pages

## Status

Active

## Context

Successor to 016 (retire v1 pages). The current Settings page is overloaded: it contains show management, show details, CSV export/import, bundle export/import, and storage diagnostics. Meanwhile, print and export actions are scattered across the Running order and Bands pages. This plan centralizes all export/print actions into a dedicated Export page and all data persistence actions into a dedicated Save page, leaving Settings focused on show configuration.

## Decisions (locked)

- **Export page** — new route `/export` containing:
  - Running order full-CSV, stage-CSV, band-CSV exports and stage/role print links.
  - Band print links (all bands, or per-band).
- **Save page** — new route `/save` containing:
  - Status card: schema version, band count, running-order count.
  - Show CSV export/import card.
  - Bands CSV export card.
  - Bundle export/import card.
- **Settings page** — retains show management and show details only.
  - Storage card removed; replaced by a collapsed-by-default Danger Zone card with Force Save and Clear All Data buttons.
  - Clear-all-data keeps its existing `ConfirmDialog`.
- **Nav menu** — adds Export and Save links.
- **RunningOrderV2** — removes export/print controls from each running order section.
- **BandListV2** — removes Print button from the band table.
- **RiderEditorV2** — removes Print and Export CSV buttons.

## Architecture rules

- Pages orchestrate; they inject services and pass data to components via `[Parameter]` / `EventCallback`.
- Components MUST NOT inject services.
- Every user-facing string MUST resolve through `ILocalizationService.T(key, args)`.
- All new localization keys MUST be added to `en.json`, `fr-fr.json`, and `LocalizationKeys.cs`.

## File-by-file scope

### New

- `Pages/Export.razor` — export/print hub.
- `Pages/Save.razor` — persistence hub.

### Modify

- `Layout/NavMenu.razor` — add `nav.export` and `nav.save` links.
- `Pages/RunningOrderV2.razor` — remove export/print UI from each running order card.
- `Pages/BandListV2.razor` — remove Print column/action.
- `Pages/RiderEditorV2.razor` — remove Print and Export CSV buttons.
- `Pages/Settings.razor` — remove Show CSV, Bands CSV, Bundle, and Storage sections; add collapsed Danger Zone section.
- `wwwroot/i18n/en.json` — new keys for pages, sections, nav.
- `wwwroot/i18n/fr-fr.json` — parity with `en.json`.
- `LocalizationKeys.cs` — constants for new keys.
- `Docs/Plans/readme.md` — add 018 to index.

## Task order

1. Create `Export.razor` and `Save.razor`.
2. Update `NavMenu.razor`.
3. Remove export/print from `RunningOrderV2.razor`, `BandListV2.razor`, `RiderEditorV2.razor`.
4. Trim `Settings.razor` and add Danger Zone.
5. Add localization keys and constants.
6. Verify build and tests.

## Implementation cadence

- **Wave 1 — New pages**: create Export and Save pages.
- **Wave 2 — Trim old pages**: remove moved functionality from RunningOrderV2, BandListV2, RiderEditorV2, Settings.
- **Wave 3 — Nav + localization**: update nav, add keys, verify parity.
- **Wave 4 — Verify**: build, test.

## Risks

- Deep links to old print routes remain valid; no breaking change there.
- Users accustomed to CSV export on the Running order page will need to navigate to Export instead.
