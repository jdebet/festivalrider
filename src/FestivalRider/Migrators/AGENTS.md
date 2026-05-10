# Migrators — Agent rules

- ONE file per `(FromVersion, ToVersion)` pair. Filename MUST be `V{from}To{to}Migrator.cs`.
- Migrators MUST be step-wise: `ToVersion == FromVersion + 1`. NEVER skip versions.
- Migrators MUST be pure: `(JsonNode raw, IList<string> warnings) -> JsonNode`. NEVER inject services, `IJSRuntime`, `ILogger`, or time.
- Migrators are FROZEN once shipped. NEVER edit a released migrator. Bug fixes ship as a successor migrator (or an explicit `vN -> vN` repair migrator authorized by a successor plan).
- NEVER reference `FestivalRider.Models` types from a migrator. Operate on `JsonNode` only so the migrator stays decoupled from current model shape.
- `StorageService` is the SOLE host of the migration pipeline. NEVER inline schema transforms anywhere else.
