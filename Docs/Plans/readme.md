# Plans

Versioned design documents for FestivalRider. Each plan captures locked decisions, scope, file-by-file intent, and implementation cadence so any contributor (human or AI) can resume work without rediscovering context.

## Conventions

- **Filename**: `NNN-kebab-title.md` with monotonic three-digit numbering. Numbers are never reused.
- **Status**: `Draft` → `Active` → `Superseded by NNN` → `Archived`.
- **Locked decisions are immutable**: anything under `## Decisions (locked)` requires a successor plan to change. Do not edit history; write a new plan that references the previous one.
- **One topic per plan**: large changes are split across successor plans rather than amended in place.
- **Prose density**: dense, no filler. Plans are reference material, not narratives.

## Index

| #   | Title                                  | Status | Summary                                                                                  |
| --- | -------------------------------------- | ------ | ---------------------------------------------------------------------------------------- |
| 001 | [Initial plan](./001-initial-plan.md)                   | Superseded by 002 (partial) | Static Blazor WASM 9 PWA on GitHub Pages — bands, riders, running order, print pipeline. Model spec + CSV sections superseded by 002; all other sections authoritative. |
| 002 | [Domain model revision](./002-domain-model-revision.md) | Superseded by 006 (partial) | Refines models: single `ShowData` root (now multi-show via 006), normalized `TravelParty`, structured `TechRider` (cables / lighting / power / FOH / monitors / stage). Drops `Genre`, `InputChannel`, `BacklineItem`. |
| 003 | [Bundle zip export/import](./003-bundle-zip-export-import.md) | Superseded by 005 (partial) | Adjunct to 002. Adds single-file `.zip` bundle (`manifest.json` + per-entity CSVs) for full `AppState` round-trip. Replace-only import, schema-version gated, 20 MB cap. Wire format authoritative; Replace-only semantics superseded by 005. |
| 004 | [Repo restructure: src/ and tests/](./004-repo-restructure-src-tests.md) | Active | Moves main project to `src/FestivalRider/` and test project to `tests/FestivalRider.Tests/`. Drops the `<Compile Remove>` workaround so the SDK glob can no longer see test sources. |
| 005 | [Bundle merge-on-import](./005-bundle-merge-import.md) | Active | Successor to 003. Adds opt-in `BundleImportMode.Merge`: upserts bands and running orders by `Guid`, preserves locally unlisted entities, remaps stage references by name, leaves `ShowData` untouched. |
| 006 | [Multi-show support](./006-multi-show-support.md) | Active | Successor to 002 (partial). Generalizes `AppState.ShowData` to `List<ShowData> Shows` + `Guid ActiveShowId`; scopes running orders by `ShowId`; bands stay global. Schema bump 2 → 3. |
| 007 | [jsPDF render swap](./007-jspdf-render.md) | Superseded by 009 (full) | Parked before implementation. Raster pipeline (`html2canvas` + `jsPDF`) cannot produce searchable / highlightable PDFs; product needs vector. See 009 for the new direction. |
| 008 | [Schema migration framework](./008-schema-migration.md) | Active | Adds an `IStateMigrator` pipeline run inside `StorageService.EnsureLoadedAsync`. Ships v1 → v2 as the first concrete migrator; reserves the v2 → v3 slot for 006. Backup-and-reset stays as the fallback. |
| 009 | [Searchable PDF export & bundles](./009-searchable-pdf-bundle.md) | Draft | Roadmap / umbrella replacing 007. Locks the direction (emit PDF primitives directly; QuestPDF as default candidate) and reserves successor plans 010 (vector engine), 011 (PDF bundles), 012 (polish) for the concrete waves. |
| 013 | [Bundle schema migration](./013-bundle-schema-migration.md) | Active | Successor to 003 / 005 / 008. Adds an `IBundleMigrator` pipeline run inside `BundleService.ImportBundle`, ships v2 → v3 as the first concrete migrator, and softens 003's hard-refuse policy to migrate-then-fallback. Bundle wire format unchanged. (010-012 reserved by 009 for the PDF roadmap.) |
| 014 | [i18n framework](./014-i18n-framework.md) | Active | Hand-rolled `ILocalizationService` + flat-JSON catalogs under `wwwroot/i18n/`. Ships `en` (source-of-truth) and `fr-FR` (wave 14h). Build-time parity test; missing-key falls back to EN. Locale lives in a separate `localStorage` key (no schema bump, never in bundles). Pins CSV / bundle wire format to `CultureInfo.InvariantCulture`. |
| 015 | [UI density and UX rework](./015-ui-density-ux.md) | Superseded by 016 | Adds parallel v2 pages (`/bands-v2`, `/running-order-v2`, `/band-v2/{Id:guid}`) with denser layouts, live filters, drag reorder, and tighter forms. Global layout collapses sidebar into top navbar. Old routes stay untouched for A/B comparison. |
| 016 | [Retire v1 pages](./016-retire-v1-pages.md) | Active | Deletes old v1 pages (`BandList`, `RunningOrder`, `RiderEditor`) and promotes v2 pages to primary routes (`/`, `/running-order`, `/band/{Id:guid}`). Simplifies nav menu. |
| 017 | [Model refactor: FOH, lighting, and cables](./017-model-refactor-foh-lighting-cables.md) | Draft | Schema bump 3 → 4. Adds `Cable.MaxLengthMeters`, `LightingMachine.Location`, `Other` escapes on `OutputProtocol`/`OutputLocation`, and replaces `FohSound.StageToFohRoundTrip` bool with `StageToFohRoundTripCount` int. Ships `V3ToV4Migrator` and `V3ToV4BundleMigrator`. |

## Template

Copy into a new `NNN-kebab-title.md` and fill in. Remove sections that genuinely don't apply; don't leave empty headings.

````markdown
# NNN — Title

## Status

`Draft` | `Active` | `Superseded by NNN` | `Archived`

## Context

One paragraph: what problem this plan solves, why now, and what changed since the previous plan (link it).

## Decisions (locked)

- **Decision name** — choice + one-line rationale.
- **Decision name** — choice + one-line rationale.

## Open questions

- Question + who owns the answer + deadline if relevant. Empty section if all locked.

## Architecture rules

Hard rules every contributor must enforce. No exceptions without a successor plan.

- Rule.
- Rule.

## File-by-file scope

Group by folder. For each file: purpose, public surface, key fields/methods. Do not paste implementation.

- `path/to/File.cs` — purpose. Public surface: `Method(...)`, `Property`. Key behavior in one line.

## Task order

Numbered, sequential. Each step must leave the app in a runnable, demoable state.

1. Step.
2. Step.

## Implementation cadence

Wave grouping for review checkpoints. Each wave maps to a contiguous slice of the task order.

- **Wave N — Title**: scope summary. Demoable outcome.

## Out of scope

Explicit non-goals so reviewers don't ask. Link to a future plan if known.

- Item.

## Risks & migrations

Known foot-guns + how the plan handles them.

- **Risk** — mitigation.
````
