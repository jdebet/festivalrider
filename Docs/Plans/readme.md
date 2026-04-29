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
| 002 | [Domain model revision](./002-domain-model-revision.md) | Active | Refines models: single `ShowData` root, normalized `TravelParty`, structured `TechRider` (cables / lighting / power / FOH / monitors / stage). Drops `Genre`, `InputChannel`, `BacklineItem`. |
| 003 | [Bundle zip export/import](./003-bundle-zip-export-import.md) | Superseded by 005 (partial) | Adjunct to 002. Adds single-file `.zip` bundle (`manifest.json` + per-entity CSVs) for full `AppState` round-trip. Replace-only import, schema-version gated, 20 MB cap. Wire format authoritative; Replace-only semantics superseded by 005. |
| 004 | [Repo restructure: src/ and tests/](./004-repo-restructure-src-tests.md) | Active | Moves main project to `src/FestivalRider/` and test project to `tests/FestivalRider.Tests/`. Drops the `<Compile Remove>` workaround so the SDK glob can no longer see test sources. |
| 005 | [Bundle merge-on-import](./005-bundle-merge-import.md) | Active | Successor to 003. Adds opt-in `BundleImportMode.Merge`: upserts bands and running orders by `Guid`, preserves locally unlisted entities, remaps stage references by name, leaves `ShowData` untouched. |

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
