---
status: Draft
title: Running Order Creation & Schedule Options Refactor
created: 2026-05-27
---

## Objective

Refactor how running orders are created and how their scheduling mode/template is configured, to make the UX clearer and reduce confusion between venue-level defaults and per-RO overrides.

## Goals

1. **Creation wizard**: Replace the current one-click "Add RO" with a modal that lets the user choose Festival vs. Venue mode, and optionally customize a festival template before creation.
2. **Schedule options**: Replace the per-RO "Template" button with a "Schedule options" modal that houses mode switching (with a warning) and the festival template editor together.
3. **Venue options**: Move the venue-level timing defaults to a shared card at the top of the Running Order V3 page, making it clear they apply to the whole venue. Keep per-RO overrides as a secondary collapsed section.

## Background

- `RunningOrderV3.razor` currently calls `BandService.AddRunningOrder(...)` directly with a default `RunningOrder`. The user never chooses schedule mode at creation time; the mode comes from `Show.DefaultScheduleMode`.
- `RunningOrderEditor.razor` embeds a "Template" button and inline mode/anchor selectors that are somewhat hidden among timing chips.
- `VenueOptionsEditor` is currently triggered per-RO via a button inside `RunningOrderEditor`, which is misleading because venue options are logically shared across ROs.

## Assumptions

- The festival "default template" means the existing `PresetFestivalMainStage` preset already available in `TemplateEditor`.
- Mode switching does **not** need a blocking confirmation dialog; a visible inline warning is sufficient.
- The `TemplateEditor` and `VenueOptionsEditor` components themselves are **not** rewritten; they are **reused** inside the new modal shells.
- Per-RO overrides for venue options are still needed, but shown as a collapsible "Overrides" panel rather than a full venue editor.

## Decisions (locked)

- All new/modified components live under `Components/`.
- All new user-facing strings go through `ILocalizationService.T(...)`; `en.json` is the source of truth.
- Every commit leaves the app compiling and tests green.

## Implementation cadence

### Wave 1 — Creation wizard

**New component: `Components/CreateRunningOrderModal.razor`**

Parameters:
- `ShowData Show`
- `bool Visible` / `EventCallback<bool> VisibleChanged`
- `EventCallback<RunningOrder> OnCreate`
- `EventCallback OnCancel`
- `Func<TimingEventType, string> GetEventLabel`
- `Func<ScheduleMode, string> GetModeLabel`

Internal state:
- `_step: PickMode | CustomTemplate`
- `_chosenMode: ScheduleMode?`
- `_workingOrder: RunningOrder` (temporary, emitted on Create)
- `_workingTemplate: FestivalTimingTemplate`

UI flow:
1. **PickMode** step
   - Two primary buttons: **Festival** / **Venue**
   - Venue → sets `ModeOverride = Traditional`, skips to Create emission
   - Festival → shows two secondary buttons:
     - **Default template** → applies `PresetFestivalMainStage` to `_workingTemplate`, skips to Create emission
     - **Custom template** → advances to `CustomTemplate` step
2. **CustomTemplate** step
   - Embeds `TemplateEditor` logic inline (or instantiates `TemplateEditor` as child component without modal shell)
   - Bottom bar: **Back** (returns to PickMode) + **Create** (emits `_workingOrder` with the composed template)

Wire-up in `Pages/RunningOrderV3.razor`:
- Replace `AddRunningOrder()` body: set `_showCreateModal = true`
- Add `CreateRunningOrderModal` instance
- `OnCreate` handler: calls `BandService.AddRunningOrder(order)`, then `Scheduler.Recalculate(order, _show)` + `BandService.UpdateRunningOrder(order)`

### Wave 2 — Schedule options modal

**New component: `Components/ScheduleOptionsModal.razor`**

Parameters:
- `RunningOrder Order`
- `ShowData Show`
- `bool Visible` / `EventCallback<bool> VisibleChanged`
- `EventCallback OnChange`
- `Func<TimingEventType, string> GetEventLabel`
- `Func<ScheduleMode, string> GetModeLabel`
- `string WarningLabel` (localized)
- `string CloseLabel`

Content:
- Mode selector (`ModeOverride`)
- Anchor event selector (`AnchorEventOverride`)
- Inline warning: *"Changing mode will recalculate all timings."*
- Festival template editor panel, visible only when `ModeOverride == Festival`
  - Reuses `TemplateEditor` as a child (no modal shell; renders inline inside the modal body)

Changes to `Components/RunningOrderEditor.razor`:
- **Remove** the following:
  - `TemplateEditor` usage and `_showTemplate` field
  - Inline mode selector (`<select>` + `OnModeChanged`)  
  - Inline anchor selector (`<select>` + `OnAnchorChanged`)
- **Keep** the timing chips (venue open/close, curfew, catering, etc.) — these are per-RO and stay in the toolbar
- **Add** a **"Schedule options"** button that opens `ScheduleOptionsModal`

### Wave 3 — Venue options card

**Changes to `Pages/RunningOrderV3.razor`**

- Add a new `RiderSection` card **above** the `@foreach (var order in ...)` loop:
  - Title: "Venue defaults"
  - Contains a compact `VenueOptionsEditor`-like panel bound to **show-level** fields:
    - `Show.DefaultScheduleMode` → keep this here? **No**, it moves to the per-RO "Schedule options" modal. Remove `DefaultScheduleMode` from the venue card.
    - `Show.DefaultAnchorEvent` → same, remove.
    - All timing defaults: `VenueOpenTime`, `VenueCloseTime`, `TechnicalGetInTime`, `DoorsOpeningTime`, `FirstShowTime`, `SoundCurfewTime`, `BackstageCurfewTime`, `BreakfastHours`, `LunchHours`, `DinnerHours`, `BreakTimeMinutes`, `SoundcheckGapMinutes`
    - `VenueOptions` (the `VenueTimingOptions` sub-object with booleans and default durations)
  - "Save venue defaults" button → calls `BandService.UpdateShow(_show)`

**Changes to `Components/RunningOrderEditor.razor`**

- Rename the current "Venue options" button to **"Override venue options"**
- The modal it opens is a **simplified** `VenueOptionsEditor` showing only fields that differ from the show default, or a clear "revert to default" action per field
- Alternatively, keep the full `VenueOptionsEditor` but pre-populate from show defaults and label it "Overrides"

## Out of scope

- `TemplateEditor` component logic — reused as-is
- `VenueOptionsEditor` component logic — reused as-is  
- `RunningOrderScheduler` logic
- Models, CSV format, persistence, bundle migration

## File scope

| File | Action |
|---|---|
| `Components/CreateRunningOrderModal.razor` | **New** |
| `Components/ScheduleOptionsModal.razor` | **New** |
| `Pages/RunningOrderV3.razor` | Add venue card, wire creation modal |
| `Components/RunningOrderEditor.razor` | Remove template/mode controls, add Schedule options button |
| `Components/TemplateEditor.razor` | Minor: allow rendering inline (not just modal) if needed |
| `wwwroot/i18n/en.json` | New keys for modal labels, buttons, warning |
| `wwwroot/i18n/fr-fr.json` | Same keys, French translations |
| `LocalizationKeys.cs` | Mirror new keys |

## Open questions

1. Should `DefaultScheduleMode` and `DefaultAnchorEvent` stay on `ShowData` or move to `VenueTimingOptions`? Currently they are on `ShowData`; this plan keeps them there but the venue card will not show them (they move to the per-RO Schedule options).
2. Should the creation wizard auto-select the first stage for the new slot, or should stage selection be part of the wizard too? Currently `AddSlot` auto-picks the first stage; this plan keeps that behavior.
3. Should the mode-change warning be a dismissible alert inside the modal, or a temporary inline toast? Inline alert is simpler.

## Verification checklist

- [ ] Creating a Venue-mode RO works with no extra steps
- [ ] Creating a Festival-mode RO with default template works
- [ ] Creating a Festival-mode RO with custom template opens the editor, then creates on validation
- [ ] Changing mode inside Schedule options shows a warning and triggers recalculate
- [ ] Venue defaults card saves correctly and affects all ROs that use show-level defaults
- [ ] Per-RO venue overrides still work
- [ ] Localization parity tests pass
