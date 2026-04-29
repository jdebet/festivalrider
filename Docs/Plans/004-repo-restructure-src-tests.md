# 004 — Repo restructure: `src/` and `tests/` layout

## Status

`Active`

## Context

The original layout (`001-initial-plan.md`) placed the main `FestivalRider.csproj` at the repo root and the test project at `FestivalRider.Tests/`. The Blazor WASM SDK's default `**/*.cs` glob, rooted at the main csproj's directory, pulled the test sources into the main compilation, surfacing as "FactAttribute not found" errors during Wave 9. The interim fix (`<Compile Remove="FestivalRider.Tests\**\*.cs" />`) papered over the symptom. This plan moves to the conventional `src/` + `tests/` split so the SDK glob can no longer see the test sources.

## Decisions (locked)

- **Layout** — main project at `src/FestivalRider/`, test project at `tests/FestivalRider.Tests/`. Solution file stays at repo root.
- **No `<Compile Remove>` workaround** — the SDK glob is rooted at `src/FestivalRider/` and cannot see `tests/`, so no manual exclusion is needed.
- **Solution paths** — `src\FestivalRider\FestivalRider.csproj` and `tests\FestivalRider.Tests\FestivalRider.Tests.csproj`.
- **Test project reference** — `..\..\src\FestivalRider\FestivalRider.csproj`.
- **CI publish path** — `dotnet publish src/FestivalRider/FestivalRider.csproj` in `.github/workflows/deploy.yml`.
- **AGENTS.md rule** — testing rule updated to `tests/FestivalRider.Tests/`.

## Architecture rules

- NEVER place new C# projects directly at the repo root. Production code goes under `src/<ProjectName>/`; tests go under `tests/<ProjectName>.Tests/`.
- NEVER reintroduce `<Compile Remove>` in `FestivalRider.csproj`. If a future glob conflict appears, fix the directory layout instead.
- Path references in plans 001 and 003 to `FestivalRider.csproj`, `Layout/`, `Pages/`, `Models/`, `Services/`, `Components/`, `PrintStrategies/`, `wwwroot/`, and `FestivalRider.Tests/` are now resolved relative to `src/FestivalRider/` (production) and `tests/FestivalRider.Tests/` (tests) respectively. The locked decisions in those plans remain authoritative for everything except path layout.

## File-by-file scope

- `FestivalRider.sln` — project paths updated to `src\FestivalRider\` and `tests\FestivalRider.Tests\`.
- `src/FestivalRider/FestivalRider.csproj` — workaround `<Compile Remove>` block deleted.
- `tests/FestivalRider.Tests/FestivalRider.Tests.csproj` — `<ProjectReference>` updated to `..\..\src\FestivalRider\FestivalRider.csproj`.
- `.github/workflows/deploy.yml` — `dotnet publish` target updated to `src/FestivalRider/FestivalRider.csproj`.
- `AGENTS.md` — testing rule path updated.

## Task order

1. `git mv` production sources into `src/FestivalRider/`; `git mv FestivalRider.Tests` to `tests/FestivalRider.Tests`.
2. Update `FestivalRider.sln` project paths.
3. Update test project's `<ProjectReference>`.
4. Remove `<Compile Remove>` from main csproj.
5. Update CI `dotnet publish` path.
6. Verify `dotnet build` and `dotnet test` are green.

## Out of scope

- Splitting models/services into separate assemblies. Single-project layout under `src/FestivalRider/` is retained.
- Renaming the main project or assembly.

## Risks & migrations

- **Risk** — IDE workspace caches stale paths. Mitigation: clear `bin/`, `obj/` after the move; reload solution.
- **Risk** — contributors with stale clones. Mitigation: this plan is the migration record; rebasing branches across the move uses `git mv` history.
