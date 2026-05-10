# BundleMigrators — Agent rules

- ONE file per `(FromVersion, ToVersion)` pair. Filename MUST be `V{from}To{to}BundleMigrator.cs`.
- Bundle migrators MUST be step-wise: `ToVersion == FromVersion + 1`. NEVER skip versions.
- Bundle migrators MUST be pure: `(BundleScratch scratch, IList<string> warnings) -> void`. NEVER inject services, `IJSRuntime`, `ILogger`, time, or `IStateMigrator`.
- Bundle migrators are FROZEN once shipped. NEVER edit a released migrator. Bug fixes ship as a successor migrator (or an explicit `vN -> vN` repair migrator authorized by a successor plan).
- NEVER reference `FestivalRider.Models` types from a bundle migrator. Operate on the manifest property bag and raw CSV strings only.
- NEVER call into `IStateMigrator` from a bundle migrator. The two pipelines stay independent.
- `BundleService` is the SOLE host of the bundle migration pipeline. NEVER inline schema transforms anywhere else.
