# 009 — Searchable PDF export & bundles

## Status

`Draft`

This plan is a **roadmap / umbrella**. It locks the high-level direction and reserves successor plan numbers (010, 011, 012) for the concrete implementation waves. It deliberately does not lock implementation-level decisions — those land in the successor plans.

## Context

Successor to [007-jspdf-render.md](./007-jspdf-render.md), which proposed a raster pipeline (`html2canvas` + `jsPDF`) producing PNG-of-text PDFs. That pipeline was reviewed before any code landed and parked: the actual product requirement is **searchable and highlightable PDFs delivered as a bundle**, which a raster pipeline cannot satisfy. `window.print()` → "Save as PDF" already produces vector/searchable PDFs through the browser's own engine; the gap is not "vector PDFs" but "**programmatic, batchable, searchable PDFs**" so a single click can package every band's rider into one zip.

The four constraints, simultaneously:

1. **Searchable / highlightable** — vector text runs in the PDF, not rasterized images of text.
2. **Programmatic** — `byte[]` returned to .NET; no per-file print dialog.
3. **Batchable** — N riders → one zip with N PDFs.
4. **Static-only** — runs entirely on GH Pages; no server, no headless Chromium.

Removing any one of these is easy. Holding all four forces the engine choice.

## Decisions (locked)

- **Direction** — emit PDF primitives directly from a structured rider definition. No DOM-rasterization path will be revisited under this plan family. `PrintAsync` (browser dialog) stays available as the user-facing fallback for one-off saves.
- **Engine candidate** — **QuestPDF** (NuGet, community edition) is the default candidate because the codebase is already 100% C# / Razor and existing models (`Band`, `RunningOrder`, `ShowData`) map directly into a fluent C# layout API with no JS interop. The candidate is confirmed or rejected by the spike in plan 010 before that plan locks; if rejected, 010's locked decision moves to `pdfmake` or `jsPDF` direct API (JS interop, JSON or imperative document tree).
- **Layout authoring is required** — none of the viable engines reads Razor. Each `IPrintStrategy` gains a parallel layout authored against the chosen engine's API. Visual fidelity to the existing Razor print pages is "best effort, not pixel-perfect"; the two surfaces will diverge over time.
- **NuGet allowance** — if QuestPDF is selected, plan 010 amends the AGENTS stack rule to permit it. Per the existing rule, no NuGet lands without a successor plan touching `## Decisions (locked)`. 010 is that vehicle.
- **Bundle format separation** — PDF bundles are a distinct artifact from CSV bundles. The CSV bundle (plans 003 / 005) is unchanged. Plan 011 picks the exact zip layout, but the two bundle kinds are not merged.
- **`window.print()` retained** — the existing `PrintAsync` path stays as a fallback. Users who want a single-page quick save keep the dialog. Plan 010 may relabel the print-page buttons but does not remove print.

## Open questions

- **License verification** — QuestPDF community edition is free under a revenue threshold. Confirmed-as-applicable check is a precondition for plan 010 locking; tracked there, not here.
- **WASM payload impact** — QuestPDF's contribution to published bundle size is reportedly 2–4 MB compressed. The 010 spike measures actual delta and decides whether the cost is acceptable. No commitment here.
- **Layout retirement** — once vector PDF lands, do the Razor print pages stay or retire? Decided in plan 012 after real usage feedback, not now.

## Architecture rules

Inherits 001 / 002 / 003 / 005 / 006 unchanged. The `// SWAP: jsPDF implementation goes here` marker rule and the AGENTS rule mandating `RenderToPdfAsync` returns `null` stay in force until plan 010 lands and explicitly amends them. No code under this plan number touches `PdfExportService.cs`.

## Roadmap (not fully specified — successor plans own the detail)

### Plan 010 — Vector PDF engine

Implements `RenderToPdfAsync` end-to-end. Picks the engine (default: QuestPDF), introduces an `IPdfLayout` abstraction parallel to `IPrintStrategy` resolved by the same `Key`, ships three concrete layouts (band rider, stage running-order, role running-order), wires DI and tests. Removes the `// SWAP` marker. Amends the root AGENTS PDF rule and (if QuestPDF) the stack rule. Print page gains a "Download PDF" button beside Print. Demoable: one click on a print page produces a single searchable PDF.

Owner of: engine choice, layout authoring contract, NuGet amendment, marker removal, AGENTS amendments.

### Plan 011 — PDF bundles

Built on 010. Adds an `IPdfBundleService` (or extends `BundleService` — decision deferred to 011) that loops the active scope (all bands / a show's stages / a show's roles), renders each via the layouts from 010, packages into a zip with a locked path scheme, streams out via `csvio.js#downloadBytes`. Includes scope-selection UX (likely on `Settings.razor` or a new export page), a cumulative-size guard, and progress feedback for batches above ~5 entries.

Owner of: bundle path scheme, scope-selection UI, size guard threshold, progress UX.

### Plan 012 — Polish & layout consolidation

Reactive plan, written only after 010 / 011 are in real users' hands. Candidate scope: retire the Razor print pages if `window.print()` usage drops to zero, embed a custom font / branding, add cover pages or tables of contents to bundle PDFs, fold per-band PDFs into the existing CSV bundle. Each item evaluated against actual usage signal, not speculation.

Owner of: layout-surface consolidation, branding, bundle-merge decision.

## Implementation cadence

Strictly serial across plans. Each plan must leave the app compiling, tested, and demoable.

- **010 first.** Single-PDF engine swap is the load-bearing risk; the spike at the top of 010 either confirms QuestPDF-in-WASM or pivots to a JS engine. Bundles cannot land without a working single-PDF path.
- **011 after 010 is stable.** Bundles inherit the engine choice and the `IPdfLayout` surface; no new engine risk.
- **012 reactive.** Do not pre-write 012; let usage drive its scope.

## Out of scope

- **DOM-rasterization revisited.** The `html2canvas` + `jsPDF` path is closed under this plan family. Any future rasterization work writes a fresh plan that supersedes 009.
- **Server-side rendering.** App is static; out of scope across the entire roadmap.
- **Editing existing PDFs** (annotations, form filling). Different problem; not on the roadmap.
- **Per-band-page page numbers across the bundle.** Each PDF stands alone. A combined-PDF cover sheet may revisit this in 012.
- **Auto-fire on load.** Inherited from 001; user-triggered only across 010 / 011.

## Risks & migrations

- **Engine spike fails.** Mitigation: 010 starts with a QuestPDF-in-WASM spike that produces a band rider PDF before any other code lands. If the spike fails, 010 pivots to `pdfmake` (JS interop) under the same `IPdfLayout` abstraction. The interface is engine-agnostic by design.
- **Layout drift between Razor and PDF.** Two parallel surfaces will diverge. Mitigation: 012 evaluates retiring the Razor print pages once download usage dominates. Documented as a known cost across 010 / 011.
- **WASM payload regression.** QuestPDF adds compressed bytes to the initial download. Mitigation: 010 spike measures the delta; if unacceptable, the plan pivots to JS interop where the engine loads on demand.
- **Bundle size on disk.** N searchable PDFs zip large. Mitigation: 011 adds a cumulative-size guard with a clear toast when exceeded.
- **License threshold breach.** QuestPDF community edition has a revenue cap. Mitigation: 010 verifies applicability before locking; if breached, 010 pivots to `pdfmake` (MIT) or another permissively-licensed engine.
