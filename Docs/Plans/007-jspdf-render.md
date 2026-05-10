# 007 — jsPDF render swap

## Status

`Superseded by 009 (full)`

Parked before implementation. The locked decision in this plan was a raster pipeline (`html2canvas` rasterizes the DOM, `jsPDF` packs the PNG into A4 pages). That output is **not searchable or highlightable** — there is no text in the PDF, only an image of text. The actual product requirement is searchable, highlightable PDFs delivered as a bundle, which a raster pipeline cannot satisfy. `window.print()` → "Save as PDF" already produces vector/searchable output, so this plan would have shipped a strictly worse artifact than what `PrintAsync` already gives users via the dialog. See [009-searchable-pdf-bundle.md](./009-searchable-pdf-bundle.md) for the new direction.

## Context

[001-initial-plan.md](./001-initial-plan.md) shipped `IPdfExportService` with a working `PrintAsync` (browser print dialog) and a stub `RenderToPdfAsync` that returns `null` and logs a warning. The `// SWAP: jsPDF implementation goes here` marker has been preserved verbatim under an AGENTS rule. Real-world usage now needs a downloadable `.pdf` artifact (email attachment, file-share archive) without forcing the user through a print dialog. This plan executes the swap: render the existing print page to a PDF in the browser via `jsPDF` + `html2canvas`, return the bytes from `RenderToPdfAsync`, and surface a "Download PDF" button on the print page.

## Decisions (locked)

- **PDF engine** — `jsPDF` + `html2canvas`, both loaded as ES modules from a pinned CDN (`https://cdn.jsdelivr.net/npm/jspdf@2.5.2/+esm` and `https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/+esm`). No NuGet package; no service-worker pre-cache (lazy-loaded, only on print routes). Pinning prevents silent CDN regressions.
- **Render path** — DOM screenshot. `html2canvas` rasterizes the `#print-root` element at scale 2; `jsPDF` packs the resulting PNG into A4 portrait pages, splitting on natural page-break heights. Vector text is sacrificed for fidelity; rider layouts are visually faithful which matters more than searchable PDF text in v1.
- **JS surface** — new file `wwwroot/js/pdf.js` exposes `window.festivalRiderPdf.renderElementToPdf(elementId, filename) -> Promise<Uint8Array>`. The function loads modules on first call (memoized) and returns the byte array; it does NOT trigger the download itself (download is composed from .NET so the existing `csvio.js#downloadBytes` stays the single download surface).
- **`RenderToPdfAsync` contract** — replaces today's stub. Returns `Task<byte[]?>`. Returns the PDF bytes on success; returns `null` and logs a warning on JS errors (network failure, render failure, oversized output). Never throws across the WASM boundary.
- **Print page UX** — `RiderPrint.razor` adds a second visible button "Download PDF" next to "Print". Click flow: spinner shown → `PdfExportService.RenderToPdfAsync` → on bytes, dispatch via `csvio.js#downloadBytes` with filename `festivalrider-{strategyKey}-{contextSlug}-{yyyyMMdd-HHmmss}.pdf`; on `null`, toast "PDF render failed; use Print instead." Print button stays unchanged.
- **No auto-fire** — user-triggered only, mirroring 001's print-button rule. Auto-render on load is explicitly out of scope.
- **Size guard** — if the rasterized PDF exceeds 25 MB, the JS function rejects (mapped to `null` in .NET) and toasts a "rider too large; use Print" message. Above expected payloads (single-band rider ≈ <2 MB).
- **Marker rule** — the `// SWAP: jsPDF implementation goes here` comment in `PdfExportService.cs` is removed by this plan. The AGENTS rule that currently mandates keeping it verbatim is amended in lockstep.
- **Service worker** — `pdf.js`, `jspdf`, `html2canvas` are NOT added to `service-worker-assets.js` precache list (kept off the offline path). First print after install requires network; subsequent calls in the same session are cached by HTTP cache.
- **Strategy parity** — every existing `IPrintStrategy` (band, stage, role) is rendered by the same DOM screenshot path; no per-strategy customization. If a strategy needs vector PDF later, that's a successor plan.

## Open questions

None.

## Architecture rules

Inherits 001 / 002 / 003 / 005 / 006 unchanged. Additional:

- `PdfExportService` MUST remain the only caller of `pdf.js`. Pages and components MUST go through the service.
- `pdf.js` MUST NOT import any FestivalRider state; it operates purely on a DOM element ID and a filename.
- Module loading MUST be lazy and memoized inside `pdf.js`. Print pages that never click "Download PDF" pay zero network cost.
- The `// SWAP: jsPDF implementation goes here` marker is REMOVED. Future PDF engine swaps amend this plan or write a successor.

## File-by-file scope

### JS interop (`src/FestivalRider/wwwroot/js`)

- `pdf.js` — new file. Exports `window.festivalRiderPdf.renderElementToPdf(elementId, filename)` returning `Promise<Uint8Array>`. Internally lazy-imports `jspdf` and `html2canvas`, captures the element, paginates onto A4 portrait, returns `pdf.output('arraybuffer')` as `Uint8Array`. Rejects on size > 25 MB.
- `AGENTS.md` — locked surface gains `festivalRiderPdf.renderElementToPdf`.

### Services (`src/FestivalRider/Services`)

- `PdfExportService.cs` — `RenderToPdfAsync` implementation: `await JS.InvokeAsync<byte[]?>("festivalRiderPdf.renderElementToPdf", elementId, filename)`; wrap in try/catch returning `null` and `_logger.LogWarning` on failure. Remove the `// SWAP` marker. `PrintAsync` unchanged.
- `IPdfExportService.cs` — XML-doc tightened to reflect "returns bytes or null"; signature unchanged.

### Pages (`src/FestivalRider/Pages`)

- `RiderPrint.razor` — wrap rendered fragment in `<div id="print-root">`; add "Download PDF" button beside "Print". On click: set busy state, call `IPdfExportService.RenderToPdfAsync`, on success dispatch `csvio.js#downloadBytes`, on null toast the failure message. Reuse the existing `IToastService`.

### Static assets (`src/FestivalRider/wwwroot`)

- `index.html` — add `<script src="js/pdf.js"></script>` after `csvio.js`.

### Root config

- `AGENTS.md` (root) — replace the rule "`IPdfExportService.RenderToPdfAsync` MUST return `null` and log a warning; KEEP the `// SWAP: jsPDF implementation goes here` marker verbatim." with: "`IPdfExportService.RenderToPdfAsync` MUST return PDF bytes via `pdf.js` or `null` (with `LogWarning`) on failure; NEVER throw across the JS boundary." Per the user-rule precedence note in `AGENTS.md`, this plan is the amending source.

### Tests (`tests/FestivalRider.Tests`)

- `PdfExportServiceTests.cs` — new file. Use `FakeJSRuntime` to assert: success path returns bytes (interop returns a stub `byte[]`), null path on JS exception (interop throws), null path on JS returning `null`. Logger receives a warning on both null paths. Tests do NOT exercise actual `jspdf`/`html2canvas` — those are JS dependencies, not unit-testable here.
- `PrintStrategyTests.cs` — no behavioral change; spot-check that strategies still render a `<div id="print-root">` wrapper if the test harness inspects markup (only if the test happens to assert the wrapper).

### Docs (`Docs/Plans`)

- `001-initial-plan.md` — leave status at `Superseded by 002 (partial)`. Append no inline edit; the print-pipeline decisions in 001 are unchanged except `RenderToPdfAsync`. Note the partial-supersession in this plan's Context (already done above).
- `readme.md` — add row for 007.

## Task order

Each step leaves the app compiling and runnable. Steps grouped impl → revisit → tests → docs.

### Implementation

1. **JS module landing.** Add `wwwroot/js/pdf.js`, register in `index.html`, update `wwwroot/js/AGENTS.md`. No .NET change yet; manual smoke from browser console: `await festivalRiderPdf.renderElementToPdf('print-root', 'test.pdf')` returns a `Uint8Array` on a print page.
2. **Service swap.** Implement `PdfExportService.RenderToPdfAsync` over the JS function; remove `// SWAP` marker; update root `AGENTS.md` rule. Build green, existing pages still call `PrintAsync` only.
3. **Print page UI.** Wrap content in `<div id="print-root">`, add "Download PDF" button. Manual e2e: download a band rider, role-print, stage-print PDFs.

### Revisit checkpoint

4. **Triage post-impl.** Inspect the produced PDFs for layout regressions (missing fonts, clipped tables, dropped print-css rules). If `html2canvas` mishandles `@media print` styles (likely — `html2canvas` reads computed screen styles, not print styles), decide whether to: (a) add a `data-pdf="true"` attribute toggling print-equivalent CSS at runtime, (b) accept the screen rendering, or (c) abort and write a successor plan switching engines. Lock the choice as an amendment to this plan's Decisions before moving to tests.

### Tests

5. **Service tests.** `PdfExportServiceTests` covering bytes/null/exception paths through `FakeJSRuntime`. Update `FakeJSRuntime` if it doesn't yet support typed-byte-array returns.
6. **Regression sweep.** Run the full suite; fix any test that asserted the old `null`-only stub behavior.

### Docs

7. **Status & index sync.** Add 007 row to `Docs/Plans/readme.md`. Confirm root `AGENTS.md` reflects the amended `RenderToPdfAsync` rule. No status flip on 001 needed (its print decisions are not invalidated wholesale).

## Implementation cadence

- **Wave 7a — JS + service (steps 1–2).** Demoable: dev-tools call returns PDF bytes; .NET path returns bytes too via a temporary debug button if needed.
- **Wave 7b — UI (step 3).** Demoable: end-user "Download PDF" works on all three strategies.
- **Wave 7c — Triage (step 4).** Reviewable as either a no-op or a Decisions amendment.
- **Wave 7d — Tests + docs (steps 5–7).** Final review checkpoint.

## Out of scope

- **Vector / searchable PDFs.** Raster only in v1; revisit if bookers complain about non-searchable archives.
- **Server-side rendering.** App is static; no server.
- **Custom paper sizes / margins.** A4 portrait fixed; controls are a successor plan.
- **PDF metadata (author, title, subject).** Defaulted by `jspdf`; not surfaced.
- **Bulk export (all bands → single PDF).** One strategy invocation per click.
- **Auto-render on load.** Explicitly off the table.

## Risks & migrations

- **`html2canvas` ignores `@media print`.** Likely. Mitigation: revisit checkpoint (step 4) chooses a path; default fallback is a `data-pdf="true"` attribute that mirrors print rules to screen-equivalent selectors during render.
- **CDN unavailable offline.** First-call PDF render fails offline; toast guides user to use Print instead. Acceptable; precaching is out of scope.
- **Pinned versions go stale.** Annual review; bumps land via this plan's amendment or a successor.
- **WASM byte marshaling cost.** `byte[]` over interop is fine for <25 MB; the size guard caps blast radius.
- **Print-page DOM drift.** If `RiderPrint.razor` ever stops wrapping in `#print-root`, render returns null. Test 5 enforces the wrapper indirectly via the success path.
