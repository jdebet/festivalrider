using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FestivalRider.BundleMigrators;
using FestivalRider.Models;
using Microsoft.Extensions.Logging;

namespace FestivalRider.Services;

public class BundleService : IBundleService
{
    public const string Format = "festivalrider-bundle";
    public const string ManifestEntry = "manifest.json";
    public const string ShowsPrefix = "shows/";
    public const string BandsPrefix = "bands/";
    public const string RunningOrdersPrefix = "running-orders/";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly IExportService _export;
    private readonly ILogger<BundleService> _logger;
    private readonly ILocalizationService _loc;
    private readonly IReadOnlyDictionary<int, IBundleMigrator> _migrators;

    public BundleService(
        IExportService export,
        ILogger<BundleService> logger,
        ILocalizationService loc,
        IEnumerable<IBundleMigrator>? migrators = null)
    {
        _export = export;
        _logger = logger;
        _loc = loc;
        _migrators = BuildMigratorIndex(migrators);
    }

    private static IReadOnlyDictionary<int, IBundleMigrator> BuildMigratorIndex(IEnumerable<IBundleMigrator>? migrators)
    {
        var dict = new Dictionary<int, IBundleMigrator>();
        if (migrators is null) return dict;
        foreach (var m in migrators)
        {
            if (m.ToVersion != m.FromVersion + 1)
                throw new InvalidOperationException(
                    $"Bundle migrator {m.GetType().Name} is not step-wise: FromVersion={m.FromVersion}, ToVersion={m.ToVersion}.");
            if (!dict.TryAdd(m.FromVersion, m))
                throw new InvalidOperationException(
                    $"Duplicate IBundleMigrator registered for FromVersion={m.FromVersion}: {dict[m.FromVersion].GetType().Name} and {m.GetType().Name}.");
        }
        return dict;
    }

    private sealed class Manifest
    {
        public string Format { get; set; } = BundleService.Format;
        public int SchemaVersion { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public List<string> Shows { get; set; } = new();
        public Guid ActiveShowId { get; set; }
        public List<string> Bands { get; set; } = new();
        public List<string> RunningOrders { get; set; } = new();
    }

    public byte[] ExportBundle(AppState state)
    {
        if (state is null) throw new ArgumentNullException(nameof(state));

        var showsSorted = state.Shows.OrderBy(s => s.Id).ToList();
        var bandsSorted = state.Bands.OrderBy(b => b.Id).ToList();
        var ordersSorted = state.RunningOrders.OrderBy(o => o.Id).ToList();

        var manifest = new Manifest
        {
            Format = Format,
            SchemaVersion = state.SchemaVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            Shows = showsSorted.Select(s => $"{ShowsPrefix}{s.Id}.csv").ToList(),
            ActiveShowId = state.ActiveShowId,
            Bands = bandsSorted.Select(b => $"{BandsPrefix}{b.Id}.csv").ToList(),
            RunningOrders = ordersSorted.Select(o => $"{RunningOrdersPrefix}{o.Id}.csv").ToList(),
        };

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var show in showsSorted)
                WriteEntry(zip, $"{ShowsPrefix}{show.Id}.csv", _export.ExportShowCsv(show));
            foreach (var band in bandsSorted)
                WriteEntry(zip, $"{BandsPrefix}{band.Id}.csv", _export.ExportBandCsv(band));
            foreach (var order in ordersSorted)
                WriteEntry(zip, $"{RunningOrdersPrefix}{order.Id}.csv", _export.ExportRunningOrderCsv(order));

            // manifest written last to ensure all entries exist
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOpts);
            WriteEntry(zip, ManifestEntry, manifestJson);
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Utf8NoBom.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
        s.Flush();
    }

    public BundleImportResult ImportBundle(Stream zipStream, BundleImportMode mode = BundleImportMode.Replace, AppState? currentState = null)
    {
        if (zipStream is null) throw new ArgumentNullException(nameof(zipStream));
        if (mode == BundleImportMode.Merge && currentState is null)
            throw new ArgumentNullException(nameof(currentState), "currentState is required for Merge mode.");
        var warnings = new List<string>();
        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            // Read every non-directory zip entry into memory so the rest of the
            // pipeline (and any migrators) can work against a single dictionary.
            IDictionary<string, string> entryTexts = ReadAllEntryTexts(archive);
            if (!entryTexts.TryGetValue(ManifestEntry, out var manifestJson))
                return Fail(_loc.T("bundle.error.missingManifest"));

            Manifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<Manifest>(manifestJson, JsonOpts);
            }
            catch (JsonException ex)
            {
                return Fail(_loc.T("bundle.error.invalidManifestJson", ex.Message));
            }
            if (manifest is null)
                return Fail(_loc.T("bundle.error.emptyManifest"));
            if (!string.Equals(manifest.Format, Format, StringComparison.Ordinal))
                return Fail(_loc.T("bundle.error.unknownFormat", manifest.Format));

            var current = new AppState();

            // Schema-version handling: idempotent short-circuit when already current,
            // hard refuse on downgrade, run the migration chain otherwise.
            if (manifest.SchemaVersion != current.SchemaVersion)
            {
                if (_migrators.Count == 0)
                {
                    _logger.LogWarning(
                        "Bundle schemaVersion {Found} does not match {Expected}; no migrators registered, refusing import.",
                        manifest.SchemaVersion, current.SchemaVersion);
                    return Fail(manifest.SchemaVersion < current.SchemaVersion
                        ? _loc.T("bundle.error.tooOld", manifest.SchemaVersion, current.SchemaVersion)
                        : _loc.T("bundle.error.tooNew", manifest.SchemaVersion, current.SchemaVersion));
                }
                if (manifest.SchemaVersion > current.SchemaVersion)
                {
                    _logger.LogWarning(
                        "Bundle schemaVersion {Found} is newer than {Expected}; downgrade not supported.",
                        manifest.SchemaVersion, current.SchemaVersion);
                    return Fail(_loc.T("bundle.error.tooNew", manifest.SchemaVersion, current.SchemaVersion));
                }

                // Pre-flight: chain must reach CurrentSchemaVersion without gaps.
                for (int v = manifest.SchemaVersion; v < current.SchemaVersion; v++)
                {
                    if (!_migrators.ContainsKey(v))
                    {
                        _logger.LogWarning(
                            "No bundle migrator covers v{From} -> v{To}; refusing import.", v, v + 1);
                        return Fail(_loc.T("bundle.error.noMigrator", manifest.SchemaVersion, current.SchemaVersion, v, v + 1));
                    }
                }

                IDictionary<string, object?> manifestDict;
                try
                {
                    manifestDict = BundleScratch.ParseManifest(manifestJson);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse manifest.json into property bag for migration.");
                    return Fail(_loc.T("bundle.error.manifestParseFailed", ex.Message));
                }

                // Scratch.Entries owns every non-manifest entry; manifest lives in
                // the dict and is re-serialized once the chain completes.
                var scratchEntries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in entryTexts)
                {
                    if (kv.Key == ManifestEntry) continue;
                    scratchEntries[kv.Key] = kv.Value;
                }
                var scratch = new BundleScratch(manifestDict, scratchEntries, manifest.SchemaVersion);

                try
                {
                    for (int v = scratch.SchemaVersion; v < current.SchemaVersion; v++)
                    {
                        var mig = _migrators[v];
                        mig.Migrate(scratch, warnings);
                        scratch.Manifest["schemaVersion"] = mig.ToVersion;
                        scratch.SchemaVersion = mig.ToVersion;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bundle migrator threw; refusing import.");
                    return Fail(_loc.T("bundle.error.migrationFailed", ex.Message));
                }

                // Re-deserialize the migrated manifest into the typed shape and
                // swap the entry-text dictionary for the migrated scratch entries.
                string migratedJson;
                try
                {
                    migratedJson = scratch.SerializeManifest();
                    manifest = JsonSerializer.Deserialize<Manifest>(migratedJson, JsonOpts);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Migrated manifest failed to round-trip.");
                    return Fail(_loc.T("bundle.error.migratedManifestInvalid", ex.Message));
                }
                if (manifest is null || manifest.SchemaVersion != current.SchemaVersion)
                    return Fail(_loc.T("bundle.error.migratedVersionMismatch"));

                entryTexts = scratch.Entries;
            }
            else
            {
                // Already current; drop the manifest entry so downstream lookups
                // work against the same shape the migration path produces.
                entryTexts.Remove(ManifestEntry);
            }

            foreach (var path in EnumerateListedPaths(manifest))
            {
                if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
                    return Fail(_loc.T("bundle.error.pathTraversal", path));
            }

            var listed = new HashSet<string>(EnumerateListedPaths(manifest), StringComparer.Ordinal);
            foreach (var name in entryTexts.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!listed.Contains(name))
                    warnings.Add(_loc.T("bundle.warning.unlisted", name));
            }

            // Shows
            if (manifest.Shows.Count == 0)
                return Fail(_loc.T("bundle.error.noShows"));
            var shows = new List<ShowData>();
            foreach (var showPath in manifest.Shows)
            {
                if (!entryTexts.TryGetValue(showPath, out var showCsv))
                    return Fail(_loc.T("bundle.error.missingShow", showPath));
                var show = _export.ImportShowCsv(showCsv);
                var pathId = ParseIdFromPath(showPath, ShowsPrefix);
                if (pathId is { } g) show.Id = g;
                shows.Add(show);
            }

            // Bands
            var bands = new List<Band>();
            foreach (var bandPath in manifest.Bands)
            {
                if (!entryTexts.TryGetValue(bandPath, out var bandCsv))
                    return Fail(_loc.T("bundle.error.missingBand", bandPath));
                bands.Add(_export.ImportBandCsv(bandCsv));
            }

            // Running orders — each decoded against the show recorded in its CSV rows.
            var showsById = shows.ToDictionary(s => s.Id);
            var orders = new List<RunningOrder>();
            foreach (var orderPath in manifest.RunningOrders)
            {
                if (!entryTexts.TryGetValue(orderPath, out var csv))
                    return Fail(_loc.T("bundle.error.missingRunningOrder", orderPath));

                var showForDecode = PeekShowIdFromCsv(csv) is { } sid && showsById.TryGetValue(sid, out var s)
                    ? s
                    : shows[0];
                var order = _export.ImportRunningOrderCsv(csv, showForDecode, bands);
                order.Id = ParseIdFromPath(orderPath, RunningOrdersPrefix) ?? order.Id;
                if (order.ShowId == Guid.Empty) order.ShowId = showForDecode.Id;
                orders.Add(order);
            }

            if (mode == BundleImportMode.Replace)
            {
                var state = new AppState
                {
                    SchemaVersion = manifest.SchemaVersion,
                    Shows = shows,
                    ActiveShowId = shows.Any(s => s.Id == manifest.ActiveShowId) ? manifest.ActiveShowId : shows[0].Id,
                    Bands = bands,
                    RunningOrders = orders,
                };
                return new BundleImportResult(state, bands.Count, orders.Count, warnings, null);
            }

            // Merge
            var (merged, stats) = MergeInto(currentState!, shows, bands, orders, warnings, _loc);
            return new BundleImportResult(
                merged,
                stats.BandsAdded + stats.BandsUpdated,
                stats.RunningOrdersAdded + stats.RunningOrdersUpdated,
                warnings,
                null,
                stats);
        }
        catch (InvalidDataException ex)
        {
            return Fail(_loc.T("bundle.error.notZip", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bundle import failed.");
            return Fail(_loc.T("bundle.error.importFailed", ex.Message));
        }

        BundleImportResult Fail(string error) =>
            new(null, 0, 0, warnings, error);
    }

    private static IEnumerable<string> EnumerateListedPaths(Manifest m)
    {
        foreach (var s in m.Shows) yield return s;
        foreach (var b in m.Bands) yield return b;
        foreach (var r in m.RunningOrders) yield return r;
    }

    private static string ReadAll(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var reader = new StreamReader(s, Utf8NoBom);
        return reader.ReadToEnd();
    }

    // Reads every non-directory zip entry into a name -> UTF-8 text map. Used
    // by ImportBundle so the rest of the pipeline (and any IBundleMigrator)
    // works against a single in-memory dictionary rather than the live archive.
    private static Dictionary<string, string> ReadAllEntryTexts(ZipArchive archive)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // skip directory entries
            dict[entry.FullName] = ReadAll(entry);
        }
        return dict;
    }

    // Parse the ShowId column out of the first data row without re-implementing CSV parsing:
    // header line is `ShowId,Stage,StartTime,...`; the first field of the next row is the Guid.
    private static Guid? PeekShowIdFromCsv(string csv)
    {
        using var sr = new StringReader(csv);
        _ = sr.ReadLine(); // header
        var first = sr.ReadLine();
        if (string.IsNullOrEmpty(first)) return null;
        var firstField = first.Split(',', 2)[0].Trim('"');
        return Guid.TryParse(firstField, out var g) ? g : null;
    }

    private static (AppState State, MergeStats Stats) MergeInto(
        AppState currentState,
        IReadOnlyList<ShowData> bundleShows,
        IReadOnlyList<Band> incomingBands,
        IReadOnlyList<RunningOrder> incomingOrders,
        List<string> warnings,
        ILocalizationService loc)
    {
        // Bands: upsert by Guid, last-write-wins by UpdatedAt.
        var bandsById = currentState.Bands.ToDictionary(b => b.Id);
        int bAdded = 0, bUpdated = 0, bSkipped = 0;
        foreach (var inc in incomingBands)
        {
            if (bandsById.TryGetValue(inc.Id, out var existing))
            {
                if (inc.UpdatedAt > existing.UpdatedAt)
                {
                    bandsById[inc.Id] = inc;
                    bUpdated++;
                }
                else
                {
                    bSkipped++;
                    warnings.Add(loc.T("bundle.warning.bandSkipped", inc.Name, inc.Id, inc.UpdatedAt.ToString("o"), existing.UpdatedAt.ToString("o")));
                }
            }
            else
            {
                bandsById[inc.Id] = inc;
                bAdded++;
            }
        }

        // Per-show stage remap. Each incoming (ShowId, StageId) must remap to a local show
        // (matched by ShowData.Name) and that show's local stage (matched by Stage.Name).
        var bundleShowById = bundleShows.ToDictionary(s => s.Id);
        var localShowByName = new Dictionary<string, ShowData>(StringComparer.OrdinalIgnoreCase);
        var ambiguousLocalShowNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in currentState.Shows)
        {
            var key = NameKey(s.Name);
            if (key.Length == 0) continue;
            if (!localShowByName.TryAdd(key, s))
                ambiguousLocalShowNames.Add(key);
        }

        var ordersById = currentState.RunningOrders.ToDictionary(o => o.Id);
        int oAdded = 0, oUpdated = 0, oSkipped = 0;

        foreach (var inc in incomingOrders)
        {
            // Resolve the sender's show by the bundle's Shows list, then match to a local show by name.
            if (!bundleShowById.TryGetValue(inc.ShowId, out var senderShow))
            {
                oSkipped++;
                warnings.Add(loc.T("bundle.warning.roNoShow", inc.Id, inc.ShowDayNumber, inc.ShowId));
                continue;
            }
            var senderShowKey = NameKey(senderShow.Name);
            if (senderShowKey.Length == 0 ||
                ambiguousLocalShowNames.Contains(senderShowKey) ||
                !localShowByName.TryGetValue(senderShowKey, out var localShow))
            {
                oSkipped++;
                warnings.Add(loc.T("bundle.warning.roNoLocalShow", inc.Id, inc.ShowDayNumber, senderShow.Name));
                continue;
            }

            // Build per-show local stage name lookup.
            var localStageByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ambiguousLocalStageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var stage in localShow.Stages)
            {
                var key = NameKey(stage.Name);
                if (key.Length == 0) continue;
                if (!localStageByName.TryAdd(key, stage.Id))
                    ambiguousLocalStageNames.Add(key);
            }
            var senderStageNameById = senderShow.Stages.ToDictionary(s => s.Id, s => s.Name ?? string.Empty);

            var remapped = new List<RunningOrderSlot>(inc.Slots.Count);
            var missing = new List<string>();
            var ambiguous = new List<string>();
            foreach (var slot in inc.Slots)
            {
                var senderName = senderStageNameById.TryGetValue(slot.StageId, out var n) ? n : string.Empty;
                var key = NameKey(senderName);
                if (key.Length == 0)
                {
                    missing.Add($"#{slot.StageId}");
                    continue;
                }
                if (ambiguousLocalStageNames.Contains(key))
                {
                    ambiguous.Add(senderName);
                    continue;
                }
                if (!localStageByName.TryGetValue(key, out var localId))
                {
                    missing.Add(senderName);
                    continue;
                }
                remapped.Add(slot with { StageId = localId });
            }

            if (missing.Count > 0 || ambiguous.Count > 0)
            {
                oSkipped++;
                var parts = new List<string>();
                if (missing.Count > 0) parts.Add($"missing local stage(s): {string.Join(", ", missing.Distinct())}");
                if (ambiguous.Count > 0) parts.Add($"ambiguous local stage name(s): {string.Join(", ", ambiguous.Distinct())}");
                warnings.Add(loc.T("bundle.warning.roMissingStages", inc.Id, inc.ShowDayNumber, string.Join("; ", parts)));
                continue;
            }

            var ro = new RunningOrder
            {
                Id = inc.Id,
                ShowId = localShow.Id,
                ShowDayNumber = inc.ShowDayNumber,
                Slots = remapped,
            };
            if (ordersById.ContainsKey(inc.Id))
            {
                warnings.Add(loc.T("bundle.warning.roReplaced", inc.Id));
                ordersById[inc.Id] = ro;
                oUpdated++;
            }
            else
            {
                ordersById[inc.Id] = ro;
                oAdded++;
            }
        }

        var mergedBands = bandsById.Values.OrderBy(b => b.Id).ToList();
        var mergedOrders = ordersById.Values.OrderBy(o => o.Id).ToList();

        var merged = new AppState
        {
            SchemaVersion = currentState.SchemaVersion,
            Shows = currentState.Shows, // never modified on Merge
            ActiveShowId = currentState.ActiveShowId,
            Bands = mergedBands,
            RunningOrders = mergedOrders,
        };
        return (merged, new MergeStats(bAdded, bUpdated, bSkipped, oAdded, oUpdated, oSkipped));
    }

    private static string NameKey(string? name) => (name ?? string.Empty).Trim();

    private static Guid? ParseIdFromPath(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var name = Path.GetFileNameWithoutExtension(path[prefix.Length..]);
        return Guid.TryParse(name, out var g) ? g : null;
    }
}
