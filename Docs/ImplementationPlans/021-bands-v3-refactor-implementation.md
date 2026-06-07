# Implementation Guide — 021 Bands V3 Refactor

> Companion to `Docs/Plans/021-bands-v3-refactor.md`. The plan is the source of truth for **decisions**;
> this document is the **step-by-step build sheet** (exact files, signatures, and copy-ready patterns).
> If the two ever disagree, **the plan wins** — fix this doc, don't diverge.
>
> Conventions used below:
> - Paths are relative to repo root `/home/jorisdebet/RiderProjects/FestivalRider`.
> - Line numbers are *approximate anchors* from exploration; always re-read the file before editing.
> - "Follow plan" decisions that differ from the obvious guess are called out in **bold**.

---

## 0. Locked shapes (read first)

These are the exact field shapes per the plan. Do not "improve" them.

| Field | Type | Notes |
| --- | --- | --- |
| `Band.DayPlaying` | `int` (default `1`) | **Not a string.** Dynamic cap validated in page/editor, not a `[Range]`. |
| `LightingRig.HasBackdrop` | `bool` | Migration derives `true` when either backdrop dim is non-null/non-zero. |
| `StageSetup.PowerOutlets` | `string?` | **Free text** (outlet count, plug type, voltage). Not an int. |
| `MonitorSetup.AmbianceMics` | `AmbianceMics` (single object, `= new()`) | **Not a `List<>`.** New model: `bool Present`, `int Count`, `CableProvider Provider`. |
| `InEarMonitor.InputType` | `InEarInputType` (`[Flags]`) | Multi-select. Always visible/editable. |
| `InEarMonitor.InputTypeOther` | `string?` | Round-trips only when `Other` flag is set. |
| `InEarMonitor.IsStereo` | `bool` | stereo = true, mono = false. |
| `ContactRole` | enum | Append `Artist, LightEngineer, Production`; **remove `BackingTech`**. |
| `PartyType` | enum | **Unchanged** (the new roles live on `ContactRole`). |
| `TimingEventType` | enum | Append `SETUP_BACKSTAGE` immediately after `SETUP_ON_STAGE`. |

Schema: **6 → 7**.

---

## Wave 1 — Plan, models, schema, migrators

### 1.1 Docs plan + index
- `Docs/Plans/021-bands-v3-refactor.md` already exists; set its `## Status` to `Active` if not already, and ensure `Docs/Plans/readme.md` index has its row. (Per `Docs/Plans/AGENTS.md`.)

### 1.2 Model edits (`src/FestivalRider/Models/`, one concept per file)

**`ContactRole.cs`** — current:
```csharp
public enum ContactRole
{
    TourManager, BandManager, FOHEngineer, MonitorEngineer, StageManager, BackingTech, Other
}
```
Target (remove `BackingTech`, append three; keep `Other` before the new ones so survivors aren't reordered beyond the removal):
```csharp
public enum ContactRole
{
    TourManager, BandManager, FOHEngineer, MonitorEngineer, StageManager, Other,
    Artist, LightEngineer, Production
}
```

**`InEarInputType.cs`** — NEW file:
```csharp
namespace FestivalRider.Models;

[Flags]
public enum InEarInputType
{
    None = 0,
    XLR = 1,
    Jack635 = 2,
    MiniJack35 = 4,
    RCA = 8,
    Other = 16,
}
```

**`InEarMonitor.cs`** — add three members (keep existing):
```csharp
public InEarInputType InputType { get; set; }
public string? InputTypeOther { get; set; }
public bool IsStereo { get; set; }
```

**`AmbianceMics.cs`** — NEW file (mutable class, data only):
```csharp
namespace FestivalRider.Models;

public class AmbianceMics
{
    public bool Present { get; set; }
    public int Count { get; set; }
    public CableProvider Provider { get; set; }
}
```

**`MonitorSetup.cs`** — add (after `InEars`):
```csharp
public AmbianceMics AmbianceMics { get; set; } = new();
```

**`LightingRig.cs`** — add (after the backdrop dims):
```csharp
public bool HasBackdrop { get; set; }
```

**`StageSetup.cs`** — add (after `BringsOwnMics`):
```csharp
public string? PowerOutlets { get; set; }
```

**`Band.cs`** — add (after `Name`/`Notes`):
```csharp
public int DayPlaying { get; set; } = 1;
```
No `[Range]` attribute — the cap is `ShowData.ShowDayCount` which is dynamic (validate in page/editor).

**`TimingEventType.cs`** — insert `SETUP_BACKSTAGE` directly after `SETUP_ON_STAGE`:
```csharp
    SETUP_ON_STAGE,
    SETUP_BACKSTAGE,
    SOUNDCHECK,
```

`PartyType.cs` — **no change.**

### 1.3 Schema constants
- `src/FestivalRider/Models/AppState.cs` (~line 5): `public int SchemaVersion { get; set; } = 7;`
- `src/FestivalRider/Services/StorageService.cs` (~line 15): `private const int CurrentSchemaVersion = 7;`
- `src/FestivalRider/Services/BundleService.cs` (~lines 92 and 128): both manifest `SchemaVersion = 7,` literals.

### 1.4 `Migrators/V6ToV7Migrator.cs` — NEW

Interface (`Migrators/IStateMigrator.cs`):
```csharp
public interface IStateMigrator
{
    int FromVersion { get; }
    int ToVersion { get; }
    JsonNode Migrate(JsonNode raw, IList<string> warnings);
}
```
Migrators are **pure** (no DI, no logging, no time), **step-wise**, and **frozen once shipped**.

`AppState` JSON shape: `root["shows"]` → each show `["bands"]` → each band `["rider"]["tech"]` with `lighting`, `monitors`, `stage`, and band-level `contacts`. Pattern to mirror is `V5ToV6Migrator.cs`.

```csharp
using System.Text.Json.Nodes;

namespace FestivalRider.Migrators;

// Plan 021. Schema 6 -> 7: Band.DayPlaying, LightingRig.HasBackdrop,
// StageSetup.PowerOutlets, MonitorSetup.AmbianceMics, InEarMonitor input fields,
// and ContactRole BackingTech -> Other remap. New enum members deserialize by name.
// FROZEN ON SHIP. Bug fixes land as a successor migrator.
public sealed class V6ToV7Migrator : IStateMigrator
{
    public int FromVersion => 6;
    public int ToVersion => 7;

    public JsonNode Migrate(JsonNode raw, IList<string> warnings)
    {
        if (raw is not JsonObject root)
            throw new InvalidOperationException("v6 payload root must be a JSON object.");

        var shows = root["shows"] as JsonArray ?? new JsonArray();
        foreach (var showNode in shows)
        {
            if (showNode is not JsonObject show) continue;
            var bands = show["bands"] as JsonArray ?? new JsonArray();
            foreach (var bandNode in bands)
            {
                if (bandNode is not JsonObject band) continue;

                // Band.DayPlaying default
                band["dayPlaying"] = 1;

                // ContactRole: BackingTech -> Other (names are stored)
                if (band["contacts"] is JsonArray contacts)
                {
                    foreach (var cNode in contacts)
                    {
                        if (cNode is JsonObject c &&
                            c["role"] is JsonValue rv && rv.TryGetValue<string>(out var role) &&
                            role == "BackingTech")
                        {
                            c["role"] = "Other";
                        }
                    }
                }

                var tech = band["rider"]?["tech"] as JsonObject;
                if (tech is null) continue;

                // LightingRig.HasBackdrop derived from existing dims
                if (tech["lighting"] is JsonObject lighting)
                {
                    bool has = IsNonZero(lighting["backdropWidthMeters"]) || IsNonZero(lighting["backdropHeightMeters"]);
                    lighting["hasBackdrop"] = has;
                }

                // StageSetup.PowerOutlets default null
                if (tech["stage"] is JsonObject stage && stage["powerOutlets"] is null)
                    stage["powerOutlets"] = null;

                // MonitorSetup.AmbianceMics default + per-IEM new fields
                if (tech["monitors"] is JsonObject monitors)
                {
                    if (monitors["ambianceMics"] is null)
                        monitors["ambianceMics"] = new JsonObject
                        {
                            ["present"] = false,
                            ["count"] = 0,
                            ["provider"] = "Venue",
                        };

                    if (monitors["inEars"] is JsonArray inEars)
                    {
                        foreach (var eNode in inEars)
                        {
                            if (eNode is not JsonObject e) continue;
                            e["inputType"] = "";        // None / no flags
                            e["inputTypeOther"] = null;
                            e["isStereo"] = false;
                        }
                    }
                }
            }
        }

        root["schemaVersion"] = 7;
        return root;
    }

    private static bool IsNonZero(JsonNode? node)
    {
        if (node is null) return false;
        if (node is JsonValue v)
        {
            if (v.TryGetValue<decimal>(out var d)) return d != 0m;
            if (v.TryGetValue<double>(out var dd)) return dd != 0d;
            if (v.TryGetValue<string>(out var s)) return !string.IsNullOrEmpty(s) && s != "0";
        }
        return false;
    }
}
```

> Note on `inputType` serialization: `[Flags]` enums round-trip in CSV as comma-joined names (see Wave 2) and in `AppState` JSON as the default System.Text.Json representation. Setting `""`/absent yields `None` on deserialize, which is the intended "no connector selected" default. Confirm by writing the migrator test (1.6) and a quick load.

### 1.5 `BundleMigrators/V6ToV7BundleMigrator.cs` — NEW

Interface (`BundleMigrators/IBundleMigrator.cs`):
```csharp
public interface IBundleMigrator
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Migrate(BundleScratch scratch, IList<string> warnings);
}
```
`BundleScratch`: `IDictionary<string, object?> Manifest`, `IDictionary<string, string> Entries` (zip path → UTF-8 CSV text), `int SchemaVersion`. Bundles store each band as `bands/{Guid}.csv` in the **same per-band CSV format** that `ExportService` emits. So this migrator must add the new rows/columns into those CSVs to match Wave 2's reader expectations — OR rely on the reader's `fallback` defaults.

Because `ExportService`'s reader uses `idx.Scalar(...)`/`GroupVal(...)` with fallbacks (missing key → empty → default), a v6 band CSV that simply lacks the new keys will still import cleanly with defaults. **The mandatory behaviors** are therefore: bump the manifest version, and remap `BackingTech` contact roles so they don't silently become the enum fallback. Mirror `V5ToV6BundleMigrator.cs` for the CSV row-rewrite helpers (`SplitCsvRows`, `SplitCsvFields`, `Escape`).

```csharp
namespace FestivalRider.BundleMigrators;

// Plan 021. Bundle schema 6 -> 7. Per-band CSVs gain optional keys that the v7
// reader defaults when absent; the one value-changing transform is the
// ContactRole BackingTech -> Other remap. Manifest version is bumped by the pipeline.
// FROZEN ON SHIP.
public sealed class V6ToV7BundleMigrator : IBundleMigrator
{
    public int FromVersion => 6;
    public int ToVersion => 7;

    public void Migrate(BundleScratch scratch, IList<string> warnings)
    {
        if (scratch is null) throw new ArgumentNullException(nameof(scratch));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        foreach (var path in scratch.Entries.Keys
                     .Where(k => k.StartsWith("bands/", StringComparison.Ordinal))
                     .ToList())
        {
            scratch.Entries[path] = RemapBackingTech(scratch.Entries[path]);
        }
    }

    // Rewrites only Contact rows whose Value == "BackingTech" under Section "Contact".
    // CSV columns are: Section,Key,Value,Index,Notes
    internal static string RemapBackingTech(string csv)
    {
        // Use the same hand-rolled split/escape helpers as V5ToV6BundleMigrator,
        // walk rows, and for Section=="Contact" && Key=="Role" && Value=="BackingTech"
        // replace Value with "Other". Re-emit byte-stable. (Copy helper methods verbatim.)
        ...
    }
}
```
> If a simpler approach is preferred and tests confirm it's safe: a no-op `Migrate` that only lets the pipeline bump the version would still import v6 bundles, but **legacy `BackingTech` would deserialize to the enum fallback (`TourManager`)** since the member no longer exists — that's data corruption. So the remap is required. Verify with the bundle migrator test.

### 1.6 Register migrators — `src/FestivalRider/Program.cs`
After the existing registrations (~lines 28 and 33):
```csharp
builder.Services.AddScoped<IStateMigrator, V6ToV7Migrator>();
builder.Services.AddScoped<IBundleMigrator, V6ToV7BundleMigrator>();
```

### 1.7 Migrator tests
- `tests/FestivalRider.Tests/Migrators/V6ToV7MigratorTests.cs` — mirror `V3ToV4MigratorTests.cs`. Cover: version props; `dayPlaying` default; `hasBackdrop` derived true/false from dims; `ambianceMics` object created; per-IEM defaults; `BackingTech → Other`; non-object root throws.
- `tests/FestivalRider.Tests/BundleMigrators/V6ToV7BundleMigratorTests.cs` — mirror `V4ToV5BundleMigratorTests.cs` using the `BuildScratch(...)` helper. Cover: version props; a `bands/{guid}.csv` containing a `Contact,Role,BackingTech,...` row is rewritten to `Other`; null-arg throws.

**Build + run migrator tests green before moving on.**

---

## Wave 2 — CSV (`ExportService.cs` + `ExportServiceTests.cs`)

All edits in `src/FestivalRider/Services/ExportService.cs`. Writer helper is `S(section, key, value, index="")`; numeric via `Inv(...)`; flags via `FormatFlags<T>` / `ParseFlags<T>`; reader via `idx.Scalar(section,key)`, `idx.Indexed(section)`, `GroupVal(g,key)`, `ParseBool`, `ParseEnum`, `NullIfEmpty`.

**Add writer + reader + test together for each field. Order matters (declaration order).**

### 2.1 Band.DayPlaying (scalar int)
Writer — Band section (after `UpdatedAt`, ~line 148):
```csharp
S("Band", "DayPlaying", Inv(b.DayPlaying));
```
Reader — Band scalar block (~line 315):
```csharp
band.DayPlaying = ParseInt(idx.Scalar("Band", "DayPlaying"), 1);
```

### 2.2 LightingRig.HasBackdrop (bool) — emit before backdrop dims per plan
Writer — Tech.Lighting (~line 192, **before** the BackdropWidth/Height lines):
```csharp
S("Tech.Lighting", "HasBackdrop", l.HasBackdrop.ToString());
```
Reader — Tech.Lighting (~line 366):
```csharp
t.Lighting.HasBackdrop = ParseBool(idx.Scalar("Tech.Lighting", "HasBackdrop"));
```

### 2.3 StageSetup.PowerOutlets (string?)
Writer — Tech.Stage (~line 258):
```csharp
S("Tech.Stage", "PowerOutlets", st.PowerOutlets ?? string.Empty);
```
Reader — Tech.Stage (~line 437):
```csharp
st.PowerOutlets = NullIfEmpty(idx.Scalar("Tech.Stage", "PowerOutlets"));
```

### 2.4 MonitorSetup.AmbianceMics (single object → scalar keys)
**Single object, not a list.** Write as scalars in a dedicated section to avoid key collisions with `Tech.Monitors`.
Writer — after the Tech.Monitors scalar block (~line 232):
```csharp
S("Tech.AmbianceMics", "Present", mo.AmbianceMics.Present.ToString());
S("Tech.AmbianceMics", "Count", Inv(mo.AmbianceMics.Count));
S("Tech.AmbianceMics", "Provider", mo.AmbianceMics.Provider.ToString());
```
Reader — after Tech.Monitors scalars (~line 410):
```csharp
mo.AmbianceMics.Present = ParseBool(idx.Scalar("Tech.AmbianceMics", "Present"));
mo.AmbianceMics.Count = ParseInt(idx.Scalar("Tech.AmbianceMics", "Count"));
mo.AmbianceMics.Provider = ParseEnum(idx.Scalar("Tech.AmbianceMics", "Provider"), CableProvider.Venue);
```

### 2.5 InEarMonitor: InputType (flags) + InputTypeOther + IsStereo
Writer — inside the `Tech.InEar` loop (after `Frequency`, ~line 253):
```csharp
S("Tech.InEar", "InputType", FormatFlags(e.InputType), idx);
if ((e.InputType & InEarInputType.Other) != 0)
    S("Tech.InEar", "InputTypeOther", e.InputTypeOther ?? string.Empty, idx);
S("Tech.InEar", "IsStereo", e.IsStereo.ToString(), idx);
```
Reader — inside the `Tech.InEar` `foreach (var g in idx.Indexed("Tech.InEar"))` object initializer (~line 425):
```csharp
InputType = ParseFlags<InEarInputType>(GroupVal(g, "InputType")),
InputTypeOther = NullIfEmpty(GroupVal(g, "InputTypeOther")),
IsStereo = ParseBool(GroupVal(g, "IsStereo")),
```

### 2.6 Enum round-trip sanity
`ContactRole.Artist/LightEngineer/Production`, `InEarInputType.*`, and `TimingEventType.SETUP_BACKSTAGE` round-trip via existing `.ToString()` / `ParseEnum` / `FormatFlags`. Add an assertion that a fresh export contains no `BackingTech` string.

### 2.7 Tests + factory
- `tests/FestivalRider.Tests/TestDataFactory.cs` `FullBand()` — populate the new fields so the round-trip test exercises them:
  - `band.DayPlaying = 2;`
  - `t.Lighting.HasBackdrop = true;`
  - `t.Stage.PowerOutlets = "4× CEE 16A, Schuko x6";`
  - `t.Monitors.AmbianceMics = new AmbianceMics { Present = true, Count = 2, Provider = CableProvider.Venue };`
  - On the existing InEar: `InputType = InEarInputType.XLR | InEarInputType.MiniJack35, IsStereo = true`.
  - Add a contact with one of the new roles (e.g. `ContactRole.Artist`).
- `tests/FestivalRider.Tests/ExportServiceTests.cs` `BandCsv_RoundTrip` — add `Assert.Equal(...)` for each new field; assert the flags value and `IsStereo`.
- **Byte-stability:** the round-trip test already re-exports and compares; ensure declaration order matches between writer and the model so `export → import → export` is identical.

**Build + run `ExportServiceTests` and `InvariantCsvUnderForeignCultureTests` green.**

---

## Wave 3 — Reusable pop-up + grid read-only refactor

### 3.1 `Components/DetailModal.razor` — NEW
No service/JS/storage injection (Components rule). Model it on `ConfirmDialog.razor` (existing Bootstrap modal). Surface:
```csharp
[Parameter] public bool Show { get; set; }
[Parameter] public string Title { get; set; } = string.Empty;
[Parameter] public RenderFragment? ChildContent { get; set; }
[Parameter] public EventCallback OnClose { get; set; }
[Parameter] public string CloseLabel { get; set; } = string.Empty; // pre-translated
```
Render a Bootstrap modal/backdrop when `Show`; close button + backdrop click → `OnClose.InvokeAsync()`. Content is whatever the caller passes (read-only).

### 3.2 Grid column template — the critical bookkeeping
The grid is a CSS grid with an **inline `grid-template-columns`** on the `.fr-grid` container in `Pages/BandListV3.razor` (~lines 42–54), currently **56 tracks**. Three files must stay byte-aligned:
1. `Pages/BandListV3.razor` — the template + both header rows (group spans + field cells).
2. `Components/BandGridRow.razor` — one cell per track, in the same order.
3. `Components/BandGridNewRow.razor` — the "add band" row; also one cell per track.

**Plan-mandated new column set** (replaces per-section editable lists with tallies/glances):
- Travel Party: one column per **existing** `PartyType` (`BandMember`, `Tech`, `Production`) → 3 columns.
- Cabling: one column per `CableType` value (`RJ45`, `BNC`, `Fiber`, `Other`) showing `X` or `X + Y` (Y = `Provider==Brought` count, in red).
- Lighting: Floor-machines Yes/No glance; Backdrop Yes/No (`HasBackdrop`) + width/height inline only when on.
- Power: single glance cell (`16A`, or `16 + 32T + 64T`, `T`=three-phase); expand → full detail.
- Monitors: Wedge count (count `DualLinked` or `Stereo` as 2), Drumfill count, IEM-brought count (`Provider==Brought`), IEM-needed/venue count (`Provider==Venue`), Ambiance Yes/No glance.
- Stage: Risers count, Other-risers count, Wireless-mics count (tallies); `PowerOutlets` (text) + `BringsOwnMics` (Yes/No) stay inline-editable.
- Contacts: first contact (role + name) glance; expand → all.

> Recompute the total track count after redefining columns, update the inline `grid-template-columns`, every `grid-column: span N` group header in header row 1, the field cells in header row 2, and the matching cells in `BandGridRow`/`BandGridNewRow`. **Change all four in lockstep in a single commit.** A mismatch silently shifts every column.

### 3.3 `Components/BandGridRow.razor`
Existing patterns to preserve:
- Whole row is one `<EditForm Model="Band" @key="Band.Id">` with `<DataAnnotationsValidator/>`.
- Parameters: `Band`, `Labels` (`BandGridLabels`), `Locked`, `Alternate`, and `EventCallback`s `OnCommit`, `OnToggleLock`, `OnEditDetail`, `OnDelete`. **No service injection.**
- Inline edit pattern: `<InputText ... @bind-Value="..." @onblur="HandleBlur" disabled="@Locked" />` where `HandleBlur() => OnCommit.InvokeAsync()`.
- Add/remove: `private void AddX() => coll.Add(new X());` and `private void RemoveX(int idx){ coll.RemoveAt(idx); OnCommit.InvokeAsync(); }`.

Changes:
- **Notes cells (Band/FOH/Monitors/Stage/Tech):** when `Locked`, render a 2-line clamped read-only `<div>` (CSS `-webkit-line-clamp:2; display:-webkit-box; -webkit-box-orient:vertical; overflow:hidden;`); when unlocked, the existing `<InputTextArea>`.
- **Tally cells** (Contacts, Travel Party, Cabling, Lighting machines, Monitors lists, Stage risers/wireless): render read-only computed counts/glance; add an expand button that sets a private `bool _show{Section}` field and renders a `<DetailModal>` with the full read-only breakdown (built from `Labels` strings). **Remove** the inline list editors for these sections.
- Keep inline-editable: Band Name, all Notes (when unlocked), Backdrop width/height (when `HasBackdrop`), `PowerOutlets`, `BringsOwnMics`, plus the lock/edit/delete buttons.
- Track expanded modals with private bool fields (allowed transient UI state).

Counting helpers (private methods on the row):
```csharp
private int WedgeCount() => Band.Rider.Tech.Monitors.Wedges.Sum(w => (w.DualLinked || w.Stereo) ? 2 : 1);
private int IemBrought() => Band.Rider.Tech.Monitors.InEars.Count(e => e.Provider == CableProvider.Brought);
private int IemNeeded()  => Band.Rider.Tech.Monitors.InEars.Count(e => e.Provider == CableProvider.Venue);
private int CableTotal(CableType ty) => Band.Rider.Tech.Cables.Count(c => c.Type == ty);
private int CableBrought(CableType ty) => Band.Rider.Tech.Cables.Count(c => c.Type == ty && c.Provider == CableProvider.Brought);
private int PartyCount(PartyType ty) => Band.TravelParty.Members.Count(m => m.Type == ty);
```

### 3.4 `Components/BandGridLabels.cs`
Add `{ get; init; }` string fields for the new columns/pop-ups: new `ContactRole` members, `InEarInputType` members, per-`CableType` headers, Power glance label, Backdrop Yes/No, Monitors split headers (wedge/drumfill/iem-brought/iem-needed), Ambiance, Stage tally headers, `PowerOutlets`, `DayPlaying`, and the modal titles + close label. Populate every one in `BandListV3.RebuildLabels()` (each via `Localization.T(...)`). **Every field added here must be assigned in `RebuildLabels` or it renders empty.**

### 3.5 `Pages/BandListV3.razor`
- Rewrite the two header rows + `grid-template-columns` (see 3.2).
- Extend `RebuildLabels()` with the new label assignments.
- `DayPlaying`: surface the active `ShowData` (the page already resolves bands for the active show) and validate `1 <= DayPlaying <= ShowData.ShowDayCount`; re-validate when `ShowDayCount` shrinks.

---

## Wave 4 — 5s auto-commit

In `Pages/BandListV3.razor` (currently writes are synchronous via `CommitBand` → `BandService.UpdateBand`; there is **no** existing debounce). Add a UI-level debounce **without** touching the `StorageService` 1s write debounce.

- Per-row `CancellationTokenSource` keyed by `Band.Id` (e.g. `Dictionary<Guid, CancellationTokenSource>`).
- On a "dirty" signal from a row input, cancel the row's pending CTS, start a new one, `await Task.Delay(5000, token)`, then `BandService.UpdateBand(band)`.
- Flush immediately on blur/lock/`Dispose` so the tail edit isn't lost.
- Update `Dispose()` to cancel/dispose all CTSs (in addition to unsubscribing `OnChange`/`OnLocaleChanged`).
- Wire the row to signal dirty: add an `EventCallback OnDirty` parameter to `BandGridRow` and invoke it from inline `@oninput`/`@bind` events (keep `OnCommit` on blur/lock as the flush path).

> Keep it simple: reuse the existing `CommitBand(band)` for the actual persist call; the debounce only controls *when* it fires.

---

## Wave 5 — Editor, scheduler, localization, tests

### 5.1 `Pages/RiderEditorV3.razor`
This is where full editing of the now-read-only-in-grid fields lives. It already uses `EditForm` + `DataAnnotationsValidator` + `ValidationSummary`, `RiderSection` blocks, and tables with `InputSelect`/`InputText`/`InputNumber`/`InputCheckbox`. Add:
- `DayPlaying` numeric input (validate against `ShowData.ShowDayCount`).
- Lighting: `HasBackdrop` `InputCheckbox`; gate the backdrop width/height inputs behind `@if (_band.Rider.Tech.Lighting.HasBackdrop)`.
- Stage: `PowerOutlets` `InputText`.
- Monitors: Ambiance mics — `InputCheckbox` (Present), `InputNumber` (Count), `InputSelect` (Provider over `CableProvider`).
- Per-IEM: **always-visible** `InputType` as a **multi-select checkbox group** over `Enum.GetValues<InEarInputType>()` excluding `None` (flags toggle pattern below), an `Other`-override `InputText` shown when the `Other` flag is set, and an `IsStereo` `InputCheckbox` (or mono/stereo select). The existing wireless `Model`/`Frequency` stay conditional.
- Contacts `InputSelect` already iterates `Enum.GetValues<ContactRole>()` with `@Localization.T($"enum.ContactRole.{r}")` — the new members appear automatically once their keys exist.

Flags checkbox-group pattern (no `InputSelect` multi-binding in Blazor):
```razor
@foreach (var flag in Enum.GetValues<InEarInputType>().Where(f => f != InEarInputType.None))
{
    var f = flag;
    <label class="me-2">
        <input type="checkbox"
               checked="@((iem.InputType & f) != 0)"
               @onchange="e => ToggleInput(iem, f, (bool)e.Value!)" />
        @Localization.T($"enum.InEarInputType.{f}")
    </label>
}
```
```csharp
private static void ToggleInput(InEarMonitor iem, InEarInputType f, bool on)
    => iem.InputType = on ? iem.InputType | f : iem.InputType & ~f;
```

### 5.2 Scheduler — wire `SETUP_BACKSTAGE` parallel to `SETUP_ON_STAGE`
Add a `bool IncludeSetupBackstage { get; set; } = true;` and `int DefaultSetupBackstageMinutes { get; set; } = 15;` to `Models/VenueTimingOptions.cs`. Then touch (each by exact analogy to the `SETUP_ON_STAGE` line already present):
- `Services/RunningOrderScheduler.cs` — the two cleanup `switch` blocks (PreShowEvents + EarlyChain) add `TimingEventType.SETUP_BACKSTAGE => !opts.IncludeSetupBackstage,`; the backward-seeding section adds an `if (opts.IncludeSetupBackstage) SeedBackward(slot, TimingEventType.SETUP_BACKSTAGE, ref cursor, opts.DefaultSetupBackstageMinutes);`.
- `Components/TemplateEditor.razor` — the three preset loaders (`LoadFestivalMainStage`, `LoadFestivalTent`, `LoadTraditionalVenue`) add a `TimingChainEntry { EventType = TimingEventType.SETUP_BACKSTAGE, DefaultDurationMinutes = 10 }` next to the existing `SETUP_ON_STAGE` entry.
- `Components/CreateRunningOrderModal.razor` — same preset addition if it builds a chain.
- `Components/ScheduleGantt.razor` — duration switch (`SETUP_BACKSTAGE => opts.DefaultSetupBackstageMinutes`), color switch (reuse amber `#ffc107`), and `LegendItems()` (`if (opts.IncludeSetupBackstage) yield return (...)`).
- `Components/EventRow.razor` — `EventBackground` color (`SETUP_BACKSTAGE => "rgba(255,193,7,0.10)"`).
- `Components/ScheduleBandPanel.razor` — duration mapping if present.
- Any `ScheduleOptionsModal.razor`/`VenueOptionsEditor.razor` that surfaces the per-event include toggles → add the `IncludeSetupBackstage` checkbox.

> The scheduler stores event types by name; no `RunningOrder` schema change beyond the enum member already covered by the schema bump.

### 5.3 Localization — `wwwroot/i18n/en.json`, `fr-fr.json`, `LocalizationKeys.cs`
Catalogs are **flat dotted-key** JSON dictionaries. `LocalizationKeys.cs` is nested static classes of `const string` whose **values mirror the JSON keys 1:1** (enforced by `LocalizationCatalogTests`). For every key you add to `en.json` you MUST add the same key to `fr-fr.json` and a matching constant in `LocalizationKeys.cs`.

Add keys:
- Enums: `enum.ContactRole.Artist`, `enum.ContactRole.LightEngineer`, `enum.ContactRole.Production`; `enum.InEarInputType.XLR`/`Jack635`/`MiniJack35`/`RCA`/`Other`; `enum.TimingEventType.SETUP_BACKSTAGE`.
- Fields/headers: `field.lighting.hasBackdrop`, `field.stage.powerOutlets`, `field.band.dayPlaying`, `field.monitors.ambianceMics*` (present/count/provider), `field.monitors.inEarInputType`, `field.monitors.inEarStereo`, `field.monitors.wedgeCount`/`drumfill`/`iemBrought`/`iemNeeded`, the per-`CableType` headers, the Power glance header, plus modal titles + close button.
- Venue option labels for `SETUP_BACKSTAGE` if surfaced in UI.

**Do NOT remove** the released `enum.ContactRole.BackingTech` key from `en.json`/`fr-fr.json`, and **keep** its `LocalizationKeys` constant — released keys are never removed even though the enum member is gone. (`LocalizationCatalogTests` enforces enum-member↔key parity for *existing* members; an extra orphan key is allowed, a missing one is not.)

> Catch: `LocalizationCatalogTests` also asserts every public enum value has an `enum.{Type}.{Value}` key. The new `InEarInputType.None` value — decide whether to ship `enum.InEarInputType.None` or whether the test excludes flags-`None`. Check the test's enum-scan (it enumerates `Enum.GetNames`); if it includes `None`, add the key (it just won't be rendered).

### 5.4 Tests
- `ExportServiceTests` — new fields (Wave 2).
- `V6ToV7MigratorTests`, `V6ToV7BundleMigratorTests` (Wave 1).
- `LocalizationCatalogTests` — must stay green (parity + enum-key coverage).
- `RunningOrderSchedulerTests` — add coverage for `SETUP_BACKSTAGE` seeding/removal mirroring existing `SETUP_ON_STAGE` tests.
- Services tested through interfaces; fake `IJSRuntime`/time; never touch real storage.

---

## Final verification

```bash
dotnet build
dotnet test
```
(Or use the `rider` MCP server per AGENTS IDE-intelligence rules.) Every commit must leave the app compiling and runnable; implement waves in order.

## Risk callouts (from the plan)
- **Grid template drift** — the 4-file column lockstep (3.2) is the highest-risk item.
- **DayPlaying dynamic cap** — validate in page/editor, re-validate on `ShowDayCount` shrink.
- **Enum ordinal stability** — `ContactRole` removal of `BackingTech` shifts ordinals; safe because names are stored and the migrator remaps. Never reorder survivors.
- **Migrator coverage** — both state + bundle migrators are mandatory or v6 data falls back to backup-and-reset / refused import.
- **Notes clamp vs edit** — clamp only the locked read-only view; reveal the textarea on unlock.
