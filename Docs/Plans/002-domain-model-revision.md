# 002 — Domain model revision

## Status

`Superseded by 006 (partial)`

The single `ShowData` root and the "multi-show out of scope" decision are superseded by 006. Section list, removals, and CSV layout from this plan still stand.

## Context

Successor to [001-initial-plan.md](./001-initial-plan.md). Refines the domain model after deeper review of the show-production workflow: single `ShowData` root holds festival-level metadata and the canonical stage list; `Band` gains a normalized `TravelParty`; `RunningOrder` loses its festival fields and references stages by `int Id`; `TechRider` is replaced wholesale with structured sections covering cabling, lighting, power, FOH sound, monitors, and stage; `InputChannel`, `BacklineItem`, `BacklineCategory`, `Band.Genre` are removed.

The architectural rules, persistence pipeline, print pipeline, and deployment story from 001 remain authoritative. Only the model file-by-file scope and the CSV section list of 001 are superseded.

## Decisions (locked)

- **AppState root** — `class AppState { int SchemaVersion = 2, ShowData ShowData, List<Band> Bands, List<RunningOrder> RunningOrders }`. Single `ShowData`; multi-show is out of scope.
- **Schema bump** — `SchemaVersion` advances 1 → 2. v1 payloads back up under `festivalrider.backup.v1` and reset (per 001's mismatch policy). No in-place migration is written.
- **Stage identity** — `Stage.Id : int`, monotonically incremented by `IBandService.AddStage(string Name) -> int`. User-visible (e.g. "Stage 1"). `RunningOrderSlot.StageId : int` references it. Deleted stages do not reuse IDs; orphaned slots surface as "Unknown stage" until reassigned.
- **RunningOrder dating** — `RunningOrder` no longer carries festival metadata. Date computed: `AppState.ShowData.DateOfOpening.AddDays(ShowDayNumber - 1)`. `ShowDayNumber` is 1-indexed.
- **TravelParty** — single normalized list: `class TravelParty { List<Party> Members }`. Each `Party` has `PartyType` (enum) + `Role` (string) + `Name`. Convenience accessors (sound tech, manager, etc.) live in services.
- **TechRider** — fully replaced. Sub-sections always-present (no nullables); empty lists / falsy bools / null strings encode "no specific need". `enum`s for closed sets, `string`s for free text, `decimal`s for meters, `int` cm for height.
- **Monitor source mutual exclusion** — `MonitorSourceMode { None, OwnConsole, FromFoh }` instead of two booleans that shouldn't overlap.
- **Closed-set enums with `Other` escape** — `CablePoint`, `CableType`, `OtherRiserType` reserve `Other`/`Custom` plus a paired `string?` override. Round-trip preserves the override.
- **Counting is service-side** — total wedges, total circuits, drumfill presence, wireless-vs-wired IEM counts are computed in services or pages from the lists; models stay logic-free per 001's architecture rules.
- **CSV format** — long-format header (`Section,Key,Value,Index,Notes`) unchanged. Section list updated; section order fixed. `ShowData`/`Stage` export as a separate "show" CSV (sections `Show` + `Show.Stage[i]`), not inside per-band CSVs. `ExportServiceTests.cs` is regenerated for the new shape.

## Open questions

None.

## Architecture rules

Inherits from 001 unchanged. Reminder: models stay data-only; counting helpers (total wedges, total circuits, wired-vs-wireless IEMs, drumfill presence) live on `IBandService` or in pages.

## File-by-file scope

Supersedes 001's `### Models` section. Everything else (services, pages, components, layout, JS interop, static assets, CI, tests) is reused verbatim from 001 unless explicitly listed below.

### Show / running order (`/Models`)

- `AppState.cs` — `class AppState { int SchemaVersion = 2, ShowData ShowData, List<Band> Bands, List<RunningOrder> RunningOrders }`. Default-constructs `ShowData`.
- `ShowData.cs` — `class ShowData { string Name, string? Address, DateOnly DateOfOpening, int ShowDayCount, List<Stage> Stages }`. `[Required]` on `Name`. `[Range(1, 31)]` on `ShowDayCount`.
- `Stage.cs` — `class Stage { int Id, string Name }`. `Id` assigned by `IBandService.AddStage`; never user-edited after creation.
- `RunningOrder.cs` — `class RunningOrder { Guid Id, int ShowDayNumber, List<RunningOrderSlot> Slots }`. `[Range(1, 31)]` on `ShowDayNumber`.
- `RunningOrderSlot.cs` — `record RunningOrderSlot(Guid BandId, int StageId, TimeOnly StartTime, int SetLengthMinutes, int ChangeoverMinutes, string? Notes)`. `StageId` was `string Stage` in 001.

### Band & travel party (`/Models`)

- `Band.cs` — `class Band { Guid Id, string Name, string? Notes, Rider Rider, List<Contact> Contacts, TravelParty TravelParty, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt }`. Drops `Genre` from 001. Default-constructs `TravelParty`.
- `TravelParty.cs` — `class TravelParty { List<Party> Members }`. Default-constructs the list.
- `Party.cs` — `class Party { PartyType Type, string Role, string Name }`. `[Required]` on `Name`.
- `PartyType.cs` — `enum { BandMember, Tech, Production }`.

### Tech rider — root (`/Models`)

`Rider` (from 001) keeps `TechRider Tech` and `HospitalityRider Hospitality`. `HospitalityRider` is unchanged.

- `TechRider.cs` — `class TechRider { List<Cable> Cables, LightingRig Lighting, PowerRequirements Power, FohSound Foh, MonitorSetup Monitors, StageSetup Stage, string? Notes }`. All sub-objects default-constructed; sub-lists default-empty.

### Cabling (`/Models`)

- `Cable.cs` — `class Cable { CablePoint Source, string? SourceOther, CablePoint Target, string? TargetOther, CableType Type, string? TypeOther, string? CategoryOrSpec, decimal? MinLengthMeters, CableProvider Provider }`. `*Other` populated only when the corresponding enum is `Other`.
- `CablePoint.cs` — `enum { SoundFoh, LightFoh, StageLeft, StageRight, StageCenter, AmpRack, MonitorWorld, Other }`.
- `CableType.cs` — `enum { Rj45, Bnc, Fiber, Other }`. `Rj45` is the default.
- `CableProvider.cs` — `enum { Venue, Brought }`. Reused across cables, mics, IEMs.

### Lighting (`/Models`)

- `LightingRig.cs` — `class LightingRig { string? OwnConsoleModel, List<LightingMachine> FloorMachines, decimal? BackdropWidthMeters, decimal? BackdropHeightMeters }`. Null `OwnConsoleModel` = no own console; null backdrop dimensions = no backdrop.
- `LightingMachine.cs` — `class LightingMachine { string Name, int Count }`.

### Power (`/Models`)

- `PowerRequirements.cs` — `class PowerRequirements { PowerAmperage Amperage, PowerPhase Phase, string? AdapterNotes }`. Defaults: `A16`, `SinglePhase`.
- `PowerAmperage.cs` — `enum { A16, A32, A63 }`.
- `PowerPhase.cs` — `enum { SinglePhase, ThreePhase }`.

### FOH sound (`/Models`)

- `FohSound.cs` — `class FohSound { string? OwnConsoleModel, OutputProtocol OutputProtocol, OutputLocation OutputLocation, string? OutputNotes, string? AdditionalHardware, int StageToFohSendCount, bool StageToFohRoundTrip, decimal? FootprintWidthMeters, decimal? FootprintLengthMeters, string? Notes }`. `OwnConsoleModel == null` = no own console; `StageToFohSendCount == 0` = no analog sends needed.
- `OutputProtocol.cs` — `enum { Aes, Analog }`.
- `OutputLocation.cs` — `enum { Foh, Stage }`.

### Monitors (`/Models`)

- `MonitorSetup.cs` — `class MonitorSetup { MonitorSourceMode SourceMode, string? OwnConsoleModel, MonitorTechLocation OwnConsoleLocation, List<MonitorWedge> Wedges, List<InEarMonitor> InEars, string? Notes }`. `OwnConsoleModel` and `OwnConsoleLocation` only meaningful when `SourceMode == OwnConsole`.
- `MonitorSourceMode.cs` — `enum { None, OwnConsole, FromFoh }`.
- `MonitorTechLocation.cs` — `enum { OnStage, OwnFootprint }`.
- `MonitorWedge.cs` — `class MonitorWedge { string Where, bool DualLinked, bool Stereo, bool DrumFill }`. `DualLinked` doubles effective wedge count; `Stereo` doubles effective circuit count (both computed service-side).
- `InEarMonitor.cs` — `class InEarMonitor { string Where, bool IsWireless, CableProvider Provider, string? Model, string? Frequency }`. `IsWireless` defaults `false`. `Model`/`Frequency` populated only when `IsWireless && Provider == Brought`.

### Stage (`/Models`)

- `StageSetup.cs` — `class StageSetup { List<Riser> Risers, List<OtherRiser> OtherRisers, List<WirelessMic> WirelessMics, bool BringsOwnMics, string? Notes }`.
- `Riser.cs` — `class Riser { string Where, decimal WidthMeters, decimal LengthMeters, int HeightCm }`. Validation: `WidthMeters` and `LengthMeters` are positive multiples of 1.0 with at least one a multiple of 2.0 (encodes the 1×2-tile assembly rule); `HeightCm >= 0` and multiple of 20.
- `OtherRiser.cs` — `class OtherRiser { string Where, OtherRiserType Type, string? Description }`. `Description` `[Required]` when `Type == Custom`.
- `OtherRiserType.cs` — `enum { EgoRiser, Custom }`.
- `WirelessMic.cs` — `class WirelessMic { string Where, int Count, CableProvider Provider, string? Model, string? Frequency }`. `Model`/`Frequency` populated only when `Provider == Brought`.

### Removed (vs 001)

Delete these files when migrating:

- `InputChannel.cs`, `BacklineItem.cs`, `BacklineCategory.cs` — TechRider rewrite removes the input list and backline tracking. Tests, CSV serialization, and any UI references must be updated alongside.

## CSV format (revised)

Long-format header `Section,Key,Value,Index,Notes` unchanged. Per-band CSV section order is fixed:

`Band, Contact, TravelParty, Tech.Cable, Tech.Lighting, Tech.LightingMachine, Tech.Power, Tech.Foh, Tech.Monitors, Tech.MonitorWedge, Tech.InEar, Tech.Stage, Tech.Riser, Tech.OtherRiser, Tech.WirelessMic, Hospitality`

`ShowData` and `Stage` export as a separate "show" CSV with sections `Show` (scalar fields of `ShowData`) and `Show.Stage[i]` (one row per stage). Within a section, keys emit in declaration order. `*Other` override strings emit only when the corresponding enum value is `Other`/`Custom`. Round-trip remains byte-stable; `ExportServiceTests.cs` is regenerated.

## Task order delta

- Wave 1 absorbs all new model files plus deletions (`InputChannel`, `BacklineItem`, `BacklineCategory`).
- Wave 2 gains `IBandService.AddStage(string Name) -> int`, `UpdateStage(Stage)`, `DeleteStage(int id)` maintaining the monotonic Stage.Id counter on `AppState.ShowData.Stages`.
- Wave 4 regenerates `ExportServiceTests.cs` against the new section list.
- Wave 6+ print strategies consume `AppState.ShowData` directly for stage labels and dates.

## Implementation cadence

Wave structure from 001 unchanged.

## Out of scope

- Multi-show support (`List<ShowData>`).
- In-place v1 → v2 migration (pre-release; backup-and-reset suffices per 001).
- Dedicated print strategy for `TravelParty` (covered inside `BandRiderPrintStrategy`).

## Risks & migrations

- **Schema 1 → 2** — v1 payloads back up under `festivalrider.backup.v1` then reset. Acceptable pre-release; explicit migrations added if the model stabilizes post-release.
- **Stage.Id reuse** — deleted stages do not reuse their IDs. Slots that referenced a deleted stage become orphaned and surface in the UI as "Unknown stage"; the editor lets the user reassign or delete affected slots.
- **`Other` override round-trip** — `CablePoint.Other` / `CableType.Other` / `OtherRiserType.Custom` round-trip the paired override string; covered by `ExportServiceTests`.
- **Riser dimensional validation** — DataAnnotations on add/edit only; CSV import assumes a trusted source and does not re-validate.
- **Downstream UI impact** — `RunningOrder.razor` must consume `AppState.ShowData` (no festival fields on `RunningOrder`) and render `ShowDayNumber` + computed date. `Settings.razor` gains a "Show details" panel editing `ShowData` (name, address, date of opening, show-day count, stage list). `RiderEditor.razor` replaces input/backline tables with the new TechRider sections. A loss-of-data warning is shown when importing a v1 CSV.
